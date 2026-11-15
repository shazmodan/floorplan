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

        let platform = platformEntries ctx

        let tables =
            tableEntries ctx recovery
            |> List.choose (fun (handler, predicates, steps) ->
                entryFor ctx handler EffectCategory.HttpIn predicates [ RouteForm.TableLookup ] steps)

        let known =
            (platform @ tables) |> List.map (fun e -> e.HandlerMember) |> Set.ofList

        let tracked =
            trackedHandlers ctx
            |> List.filter (fun h -> not (known.Contains h.Handler.Display))
            |> List.distinctBy (fun h -> h.Handler.Key, h.Caller.Ref.Key, h.CallIndex)
            |> trackedEntries ctx recovery

        platform @ tables @ tracked)
    |> List.distinctBy (fun e -> e.ApplicationId, e.HandlerMember, e.Route, e.HttpMethod)
    |> List.sortBy sortKey
    |> List.mapi (fun i e -> { e with Id = $"entry-{i + 1}" })
