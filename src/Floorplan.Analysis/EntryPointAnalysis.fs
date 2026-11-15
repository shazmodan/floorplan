/// Entry points of every application: handlers from platform registrations (form C), from tables (form B)
/// and from request-data tracking with the conditions on the way to them (form A).
module Floorplan.Analysis.EntryPointAnalysis

open Floorplan.Model
open Floorplan.IL
open Floorplan.Analysis.Taint
open Floorplan.Analysis.EntryPoints
open Floorplan.Analysis.RouteRecovery

let private entryFor
    (ctx: AppContext)
    (handler: MethodRef)
    (category: EffectCategory)
    (predicates: Predicate list)
    (forms: RouteForm list)
    (steps: EvidenceStep list)
    : EntryPoint option =
    let set = ctx.Program.Set
    let point = firstPoint set handler

    let projectId =
        match set.Origin handler.Type.Assembly with
        | User pid -> pid
        | _ -> ctx.Project.ProjectId

    ctx.Program.Attributor.Attribute(projectId, handler, point)
    |> Option.map (fun unit ->
        let httpMethod, route, completeness = assemble predicates

        { Id = ""
          ApplicationId = ctx.Project.ProjectId
          HandlerUnit = unit.Id
          HandlerMember = handler.Display
          Location = point |> Option.map Attribution.toLocation
          Category = category
          Predicates = predicates
          HttpMethod = httpMethod
          Route = route
          Completeness = completeness
          Forms = if predicates.IsEmpty then [] else forms
          Evidence = { Steps = steps } })

/// Entry points for handlers found by request-data tracking, one per call path, with their predicates.
let private trackedEntries (ctx: AppContext) (recovery: Recovery) (handlers: FoundHandler list) =
    handlers
    |> List.collect (fun h ->
        let baseEntry = unresolvedEntry ctx h

        match baseEntry with
        | None -> []
        | Some e ->
            let local =
                if h.Guards.IsEmpty then [] else recovery.PathPredicates(h.Caller, h.CallIndex)

            recovery.Upward(h.Caller, 0)
            |> List.map (fun up ->
                let compared = (local @ up) |> List.distinct
                let parameters = if h.Guards.IsEmpty then [] else recovery.Parameters(h.Caller, h.CallIndex, compared)
                compared @ parameters)
            |> List.distinct
            |> List.choose (fun predicates -> entryFor ctx h.Handler e.Category predicates [ RouteForm.ControlFlow ] e.Evidence.Steps))

/// For one handler: entries without any condition are dropped when another has some, and an entry whose
/// conditions are all contained in another entry's adds nothing.
let private prune (es: EntryPoint list) =
    let es =
        if es |> List.exists (fun e -> e.Route.IsSome || not e.Predicates.IsEmpty) then
            es |> List.filter (fun e -> e.Route.IsSome || not e.Predicates.IsEmpty)
        else
            es

    let shape (e: EntryPoint) =
        e.Predicates |> List.map (fun p -> p.Part, p.Operator, p.Index, p.Value) |> Set.ofList

    es
    |> List.filter (fun e ->
        not (es |> List.exists (fun other -> not (obj.ReferenceEquals(other, e)) && Set.isProperSubset (shape e) (shape other))))

let private sortKey (e: EntryPoint) =
    e.ApplicationId, defaultArg e.Route "~", defaultArg e.HttpMethod "", e.HandlerMember

/// All entry points of all applications.
let analyze (program: EffectAnalysis.Program) (input: AnalysisInput) : EntryPoint list =
    input.Projects
    |> List.filter (fun p -> p.IsApplication)
    |> List.collect (fun project ->
        let methods = reachableUserMethods program project
        let matcher = program.MatcherFor project
        let taint = Taint.analyze program.Set program.Graph matcher program.Recognizers (List.ofSeq methods.Values)

        let ctx =
            { Project = project
              Program = program
              Matcher = matcher
              Methods = methods
              Taint = taint }

        let recovery = Recovery ctx

        let platform = platformEntries ctx @ controllerEntries ctx

        let tables =
            tableEntries ctx recovery
            |> List.choose (fun (handler, predicates, steps) ->
                entryFor ctx handler EffectCategory.HttpIn predicates [ RouteForm.TableLookup ] steps)

        let known =
            (platform @ tables) |> List.map (fun e -> e.HandlerMember) |> Set.ofList

        let foundHandlers = trackedHandlers ctx

        let tracked =
            foundHandlers
            |> List.filter (fun h -> not (known.Contains h.Handler.Display))
            |> List.distinctBy (fun h -> h.Handler.Key, h.Caller.Ref.Key, h.CallIndex)
            |> trackedEntries ctx recovery

        // Routing inside dependencies, found by running the application symbolically. A user function the
        // platform reaches unconditionally that dispatches on request data itself is a router, not a handler:
        // its handlers come from the analysis above.
        let routers =
            foundHandlers |> List.filter (fun h -> not h.Guards.IsEmpty) |> List.map (fun h -> h.Caller.Ref.Display) |> Set.ofList

        let symbolic =
            FrameworkRouting.analyze ctx
            |> List.filter (fun e -> not (e.Predicates.IsEmpty && routers.Contains e.HandlerMember))
            // Several symbolic paths to one handler: keep the most specific ones.
            |> List.groupBy (fun e -> e.HandlerMember)
            |> List.collect (fun (_, es) -> prune es)

        // A symbolic entry with no conditions adds nothing for a handler other analyses already report.
        let reported = (platform @ tables @ tracked) |> List.map (fun e -> e.HandlerMember) |> Set.ofList

        let symbolic =
            symbolic |> List.filter (fun e -> not (e.Predicates.IsEmpty && reported.Contains e.HandlerMember))

        platform @ tables @ tracked @ symbolic)
    |> List.distinctBy (fun e -> e.ApplicationId, e.HandlerMember, e.Route, e.HttpMethod)
    |> List.sortBy sortKey
    |> List.mapi (fun i e -> { e with Id = $"entry-{i + 1}" })
