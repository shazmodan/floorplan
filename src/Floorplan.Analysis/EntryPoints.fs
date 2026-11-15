/// Entry points (plan section 6): user code reachable from an inbound-connection primitive that receives data
/// derived from the request. Handlers come from platform registrations (form C) and from request-data
/// tracking: a handler is a user function invoked on a branch chosen by request data, or, where nothing
/// branches on it, the first user function that receives it.
module Floorplan.Analysis.EntryPoints

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog
open Floorplan.Analysis.Taint

/// A handler found by request-data tracking, before routes are recovered.
type FoundHandler =
    { Handler: MethodRef
      /// The method and call instruction where the handler is invoked.
      Caller: MethodDef
      CallIndex: int
      /// Conditional branches on request data that decide whether the call runs (instruction indices).
      Guards: int list
      Tags: Tags }

type AppContext =
    { Project: AnalyzedProject
      Program: EffectAnalysis.Program
      Matcher: Matcher
      /// User methods reachable from the application, by key.
      Methods: IReadOnlyDictionary<string, MethodDef>
      Taint: TaintResult }

let private step (description: string) (m: MethodRef) (point: SourcePoint option) =
    { Description = description
      Member = m.Display
      Assembly = m.Type.Assembly
      Location = point |> Option.map Attribution.toLocation }

let firstPoint (set: AssemblySet) (m: MethodRef) =
    match set.TryMethod m with
    | Some(def, _) ->
        match def.Body.Value with
        | Some body -> body.Instructions |> Array.tryPick (fun i -> i.Point)
        | None -> None
    | None -> None

let private isAccessorOrCtor (m: MethodRef) =
    MethodRef.isConstructor m || m.Name.StartsWith "get_" || m.Name.StartsWith "set_"

/// User methods reachable from an application's own assembly, through user code only.
let reachableUserMethods (program: EffectAnalysis.Program) (project: AnalyzedProject) =
    let set = program.Set
    let result = Dictionary<string, MethodDef>()
    let stack = Stack<MethodRef>()

    match program.Users |> List.tryFind (fun (p, _) -> p.ProjectId = project.ProjectId) with
    | Some(_, assembly) ->
        for t in assembly.Types do
            for m in t.Methods do
                stack.Push m.Ref
    | None -> ()

    while stack.Count > 0 do
        let m = stack.Pop()

        if not (result.ContainsKey m.Key) && set.IsUser m.Type.Assembly then
            match set.TryMethod m with
            | Some(def, _) when def.Body.Value.IsSome ->
                result[m.Key] <- def

                for e in program.Graph.Callees m do
                    if set.IsUser e.Target.Type.Assembly && not (result.ContainsKey e.Target.Key) then
                        stack.Push e.Target
            | _ -> ()

    result :> IReadOnlyDictionary<string, MethodDef>

/// Instruction offsets whose values feed the given values, within one method (through any instruction).
let rec private contributors (flow: Flow) (depth: int) (acc: HashSet<int>) (values: Values) =
    if depth < 64 then
        for v in values do
            match v with
            | Pushed o when acc.Add o ->
                for operand in flow.OperandsAt o do
                    contributors flow (depth + 1) acc operand
            | _ -> ()

/// The conditional branches of a method whose condition carries request data.
let taintedBranches (ctx: AppContext) (m: MethodDef) : (int * Tags) list =
    match m.Body.Value with
    | None -> []
    | Some body ->
        body.Instructions
        |> Array.mapi (fun i ins -> i, ins)
        |> Array.choose (fun (i, ins) ->
            match ins.Op with
            | BrTrue _
            | BrFalse _
            | BrCmp _
            | Switch _ ->
                let tags = ctx.Taint.OperandTags m.Ref ins.Offset |> Set.unionMany
                if tags.IsEmpty then None else Some(i, tags)
            | _ -> None)
        |> Array.toList

let private userTargets (ctx: AppContext) (m: MethodDef) (ins: Instruction) (declared: MethodRef) =
    let edges =
        ctx.Program.Graph.Callees m.Ref
        |> List.filter (fun e -> e.Offset = ins.Offset && (e.Kind = Direct || e.Kind = Virtual))
        |> List.map (fun e -> e.Target)

    (if edges.IsEmpty then [ declared ] else edges)
    |> List.filter (fun t -> ctx.Methods.ContainsKey t.Key)

/// Calls in `m` that run only on some outcomes of a request-data branch, excluding calls that compute the
/// branch conditions themselves (accessors, active patterns, comparisons).
let guardedCalls (ctx: AppContext) (m: MethodDef) : (int * MethodRef * int list) list =
    match m.Body.Value, DataFlow.ofMethod m with
    | Some body, Some flow ->
        let branches = taintedBranches ctx m

        if branches.IsEmpty then
            []
        else
            let cfg = ControlFlow.build body
            let conditionCalls = HashSet<int>()

            for (b, _) in branches do
                for operand in flow.OperandsAt body.Instructions[b].Offset do
                    contributors flow 0 conditionCalls operand

            body.Instructions
            |> Array.mapi (fun i ins -> i, ins)
            |> Array.collect (fun (i, ins) ->
                match ins.Op with
                | Call d
                | CallVirt d when not (conditionCalls.Contains ins.Offset) && not (isAccessorOrCtor d) ->
                    let guards = branches |> List.map fst |> List.filter (fun b -> ControlFlow.isControlledBy cfg b i)

                    if guards.IsEmpty then
                        [||]
                    else
                        userTargets ctx m ins d
                        |> List.filter (fun t -> not (isAccessorOrCtor t))
                        |> List.map (fun t -> i, t, guards)
                        |> List.toArray
                | _ -> [||])
            |> Array.toList
    | _ -> []

/// Handlers found by request-data tracking (plan section 6.3, step 3).
let trackedHandlers (ctx: AppContext) : FoundHandler list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key
    let unwrap = PlatformRouting.unwrap set isUser 0

    let guarded =
        ctx.Methods.Values
        |> Seq.collect (fun m ->
            guardedCalls ctx m
            |> List.map (fun (i, t, guards) ->
                let tags =
                    guards
                    |> List.collect (fun g -> ctx.Taint.OperandTags m.Ref m.Body.Value.Value.Instructions[g].Offset)
                    |> Set.unionMany

                { Handler = unwrap t
                  Caller = m
                  CallIndex = i
                  Guards = guards
                  Tags = tags }))
        |> Seq.toList

    // A function that itself dispatches on request data is a router: its own guarded calls are the handlers.
    let routers =
        guarded |> List.map (fun g -> g.Caller.Ref.Key) |> Set.ofList

    let branchHandlers = guarded |> List.filter (fun g -> not (routers.Contains g.Handler.Key))

    // Sources whose data never decides a branch: the first user function that receives the data handles it.
    let dispatching =
        ctx.Methods.Values |> Seq.filter (fun m -> not (taintedBranches ctx m).IsEmpty) |> Seq.map (fun m -> m.Ref.Key) |> Set.ofSeq

    let receivers =
        ctx.Taint.Sources
        |> List.filter (fun s -> ctx.Methods.ContainsKey s.Method.Key)
        |> List.distinctBy (fun s -> s.Method.Key)
        |> List.collect (fun s ->
            // Everything downstream of the source, through calls that pass request data along.
            let downstream = HashSet<string>([ s.Method.Key ])
            let stack = Stack<string>([ s.Method.Key ])

            while stack.Count > 0 do
                let k = stack.Pop()

                for e in ctx.Program.Graph.Callees ctx.Methods[k].Ref do
                    if isUser e.Target
                       && ctx.Taint.Parameters.ContainsKey e.Target.Key
                       && downstream.Add e.Target.Key then
                        stack.Push e.Target.Key

            if downstream |> Seq.exists dispatching.Contains then
                []
            else
                let m = ctx.Methods[s.Method.Key]
                let body = m.Body.Value.Value
                let tags = Set.singleton (match s.Rule.RequestPart with Some p -> Part p | None -> if s.Rule.Category = Some EffectCategory.Network then Connection else HttpContext)

                let firstReceiver =
                    body.Instructions
                    |> Array.mapi (fun i ins -> i, ins)
                    |> Array.tryPick (fun (i, ins) ->
                        match ins.Op with
                        | Call d
                        | CallVirt d when not (isAccessorOrCtor d) ->
                            let carries = ctx.Taint.OperandTags m.Ref ins.Offset |> List.exists (fun t -> not t.IsEmpty)

                            if carries then
                                userTargets ctx m ins d
                                |> List.filter (fun t -> not (isAccessorOrCtor t))
                                |> List.tryHead
                                |> Option.map (fun t -> i, t)
                            else
                                None
                        | _ -> None)

                match firstReceiver with
                | Some(i, t) ->
                    [ { Handler = unwrap t
                        Caller = m
                        CallIndex = i
                        Guards = []
                        Tags = tags } ]
                | None ->
                    [ { Handler = m.Ref
                        Caller = m
                        CallIndex = 0
                        Guards = []
                        Tags = tags } ])

    branchHandlers @ receivers

let categoryOf (tags: Tags) =
    if not tags.IsEmpty && tags |> Set.forall (fun t -> t = Connection) then
        EffectCategory.Network
    else
        EffectCategory.HttpIn

/// An entry point for a tracked handler, with no route yet (Phase 3): unresolved.
let unresolvedEntry (ctx: AppContext) (h: FoundHandler) : EntryPoint option =
    let set = ctx.Program.Set
    let point = firstPoint set h.Handler

    let projectId =
        match set.Origin h.Handler.Type.Assembly with
        | User pid -> pid
        | _ -> ctx.Project.ProjectId

    match ctx.Program.Attributor.Attribute(projectId, h.Handler, point) with
    | None -> None
    | Some unit ->
        let body = h.Caller.Body.Value.Value
        let sources = ctx.Taint.Sources |> List.truncate 3

        let steps =
            [ for s in sources -> step $"request data enters through {s.Rule.Id} in {s.Method.Display}" s.Method s.Point
              for g in h.Guards -> step $"{h.Caller.Ref.Display} branches on request data" h.Caller.Ref (Instruction.pointAt body.Instructions g)
              yield step $"{h.Caller.Ref.Display} calls the handler {h.Handler.Display}" h.Caller.Ref (Instruction.pointAt body.Instructions h.CallIndex) ]

        Some
            { Id = ""
              ApplicationId = ctx.Project.ProjectId
              HandlerUnit = unit.Id
              HandlerMember = h.Handler.Display
              Location = point |> Option.map Attribution.toLocation
              Category = categoryOf h.Tags
              Predicates = []
              HttpMethod = None
              Route = None
              Completeness = Completeness.Unresolved
              Forms = []
              Evidence = { Steps = steps } }

/// Entry points registered with the platform's routing (form C).
let platformEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.choose (fun r ->
        let handlerSpec = r.Rule.Payload |> List.tryFind (fun p -> p.Role = "handler")
        let templateSpec = r.Rule.Payload |> List.tryFind (fun p -> p.Role = "route-template")

        match handlerSpec, templateSpec with
        | Some hs, Some ts ->
            let operands = r.Flow.OperandsAt r.Offset
            let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set)
            let handlerOperand = List.tryItem (Values.operandIndex r.Declared false hs.Argument) operands

            match handlerOperand |> Option.bind (PlatformRouting.handlerOf set r.Flow) with
            | None -> None
            | Some target ->
                let handler = PlatformRouting.unwrap set isUser 0 target
                let point = firstPoint set handler
                let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

                let projectId =
                    match set.Origin handler.Type.Assembly with
                    | User pid -> pid
                    | _ -> ctx.Project.ProjectId

                match ctx.Program.Attributor.Attribute(projectId, handler, point) with
                | None -> None
                | Some unit ->
                    let template = evaluator.Operand(r.Offset, Values.operandIndex r.Declared false ts.Argument)

                    let prefix =
                        match operands with
                        | builder :: _ -> PlatformRouting.prefixOf ctx.Matcher evaluator r.Flow 0 builder
                        | [] -> Some []

                    let httpMethod =
                        match r.Rule.HttpMethod with
                        | Some m -> Some m
                        | None ->
                            r.Rule.Payload
                            |> List.tryFind (fun p -> p.Role = "http-methods")
                            |> Option.bind (fun spec ->
                                match evaluator.Operand(r.Offset, Values.operandIndex r.Declared false spec.Argument) with
                                | Values.Text m -> Some m
                                | Values.Parts parts ->
                                    let texts = parts |> List.choose (function Values.Text t -> Some t | _ -> None)
                                    if texts.Length = parts.Length && not texts.IsEmpty then Some(String.concat "," texts) else None
                                | _ -> None)

                    let location = registrationPoint |> Option.map Attribution.toLocation

                    let route, predicates, completeness =
                        match template, prefix with
                        | Values.Text t, Some p ->
                            // Method and full path known: complete. A template without a method (Map): partial.
                            let route = PlatformRouting.normalizeRoute (p @ [ t ])
                            Some route, PlatformRouting.templatePredicates route location, (if httpMethod.IsSome then Completeness.Complete else Completeness.Partial)
                        | _ -> None, [], (if httpMethod.IsSome then Completeness.Partial else Completeness.Unresolved)

                    let methodPredicate =
                        httpMethod
                        |> Option.map (fun m ->
                            { Part = RequestPart.Method
                              Operator = PredicateOperator.Equals
                              Index = None
                              Value = m
                              Location = location })
                        |> Option.toList

                    Some
                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = handler.Display
                          Location = point |> Option.map Attribution.toLocation
                          Category = EffectCategory.HttpIn
                          Predicates = methodPredicate @ predicates
                          HttpMethod = httpMethod
                          Route = route
                          Completeness = completeness
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{r.Caller.Ref.Display} registers {handler.Display} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint ] } }
        | _ -> None)
