/// Dependency summaries (plan section 5, step 2): for each dependency method, the catalog rules it can reach,
/// each with the shortest call path to the primitive. Computed by breadth-first search backwards from every
/// sink call site, so the first path found is the shortest.
module Floorplan.Analysis.Summaries

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

/// A call that is an effect: a catalog sink, or a P/Invoke declared outside the base libraries.
type SinkHit =
    { Rule: Rule
      Category: EffectCategory
      /// The base-library member (or native declaration) that performs the effect.
      Primitive: MethodRef }

let nativeRule =
    { Id = "native.pinvoke"
      Assembly = ""
      Type = ""
      Members = [ "*" ]
      IncludeDerivedTypes = false
      Kind = Sink
      Category = Some EffectCategory.Native
      Payload = []
      HttpMethod = None
      RequestPart = None
      WhenFirstParameter = None
      BlindSpot = None
      ComparesTo = None
      Dispatch = None
      LogCall = LogCall.Direct
      Configuration = None
      Source = "built-in" }

/// Whether a call edge is itself an effect.
let sinkOf (set: AssemblySet) (matcher: Matcher) (edge: CallEdge) : SinkHit option =
    match matcher.Sink edge.Declared with
    | Some rule ->
        Some
            { Rule = rule
              Category = rule.Category.Value
              Primitive = edge.Declared }
    | None ->
        match set.Origin edge.Target.Type.Assembly with
        | Framework -> None
        | _ ->
            match set.TryMethod edge.Target with
            | Some(def, _) when def.PInvokeModule.IsSome ->
                Some
                    { Rule = nativeRule
                      Category = EffectCategory.Native
                      Primitive = edge.Target }
            | _ -> None

/// How a method reaches a rule: through this edge, which is either the sink call itself or a call to a method
/// that reaches the rule in Distance - 1 more steps.
type Witness =
    { Distance: int
      Edge: CallEdge
      Hit: SinkHit }

/// One call on the way from a dependency method to a primitive, as kept in evidence and in the cache.
type PathStep =
    { Caller: string
      CallerAssembly: string
      Declared: string
      Virtual: bool }

/// The payload of the primitive a dependency method reaches, in terms of the method's arguments.
type PayloadOrigin = { Role: string; Origin: Values.Origin }

/// A rule a dependency method can reach, with the shortest path to it.
type DependencyEffect =
    { RuleId: string
      Category: EffectCategory
      Primitive: string
      PrimitiveAssembly: string
      Steps: PathStep list
      /// None when the rule has no payload.
      Payload: PayloadOrigin option }

let toStep (e: CallEdge) =
    { Caller = e.Caller.Display
      CallerAssembly = e.Caller.Type.Assembly
      Declared = e.Declared.Display
      Virtual = e.Kind = Virtual }

type Summaries(graph: CallGraph, matcher: Matcher, recognizers: ICodegenRecognizer list, producers: Values.Producers, isSummarized: MethodRef -> bool) =
    let set = graph.Set
    let best = Dictionary<string, Dictionary<string, Witness>>()
    let chosen = Dictionary<struct (string * string), Witness * Values.Origin>()
    let flows = Dictionary<string, DataFlow.Flow option>()

    // A log call in a dependency through a library that dispatches to its providers' loggers (Microsoft's): what
    // the sinks do is the logging configuration's, not the dependency's. It is not followed, just as where the
    // library is part of the shared framework; a log call in the application reaches the sinks itself.
    let followed (e: CallEdge) =
        e.Kind <> Initializer
        && not (matcher.Logger e.Declared |> Option.exists (fun r -> r.Dispatch.IsSome))

    let witnesses (m: MethodRef) =
        match best.TryGetValue m.Key with
        | true, d -> d
        | _ ->
            let d = Dictionary()
            best[m.Key] <- d
            d

    do
        let methods = graph.ReachableMethods |> List.filter isSummarized
        let callers = Dictionary<string, ResizeArray<CallEdge>>()
        let queue = Queue<MethodRef * string>()

        for m in methods do
            for e in graph.Callees m |> List.filter followed do
                match sinkOf set matcher e with
                | Some hit ->
                    let w = witnesses m

                    if not (w.ContainsKey hit.Rule.Id) then
                        w[hit.Rule.Id] <- { Distance = 1; Edge = e; Hit = hit }
                        queue.Enqueue((m, hit.Rule.Id))
                | None ->
                    if isSummarized e.Target then
                        match callers.TryGetValue e.Target.Key with
                        | true, l -> l.Add e
                        | _ -> callers[e.Target.Key] <- ResizeArray [ e ]

        while queue.Count > 0 do
            let m, ruleId = queue.Dequeue()
            let known = (witnesses m)[ruleId]

            match callers.TryGetValue m.Key with
            | true, incoming ->
                for e in incoming do
                    let w = witnesses e.Caller

                    if not (w.ContainsKey ruleId) then
                        w[ruleId] <-
                            { Distance = known.Distance + 1
                              Edge = e
                              Hit = known.Hit }

                        queue.Enqueue((e.Caller, ruleId))
            | _ -> ()

    /// A rule's payload spec, when it has one.
    let payloadSpec (w: Witness) = w.Hit.Rule.Payload |> List.tryHead

    let flowOf (m: MethodRef) =
        match flows.TryGetValue m.Key with
        | true, f -> f
        | _ ->
            let f = set.TryMethod m |> Option.bind (fun (def, _) -> DataFlow.ofMethod def)
            flows[m.Key] <- f
            f

    /// Every rule the method can reach, with its witness for each: the shortest, or for a rule with a payload the
    /// shortest whose payload is not entirely unknown (`Choose`), so evidence follows the payload's path.
    member this.Reaches(m: MethodRef) : Witness list =
        match best.TryGetValue m.Key with
        | true, d ->
            d.Values
            |> Seq.map (fun w -> if (payloadSpec w).IsSome then fst (this.Choose(m, w.Hit.Rule.Id, 0)) else w)
            |> Seq.sortBy (fun w -> w.Distance, w.Hit.Rule.Id)
            |> Seq.toList
        | _ -> []

    /// The call path from `m` to the primitive of `ruleId`: each edge in order, the last one being the sink call.
    member this.Path(m: MethodRef, ruleId: string) : CallEdge list =
        let rec walk (m: MethodRef) (acc: CallEdge list) (guard: int) =
            if guard > 64 then
                List.rev acc
            else
                match best.TryGetValue m.Key with
                | true, d when d.ContainsKey ruleId ->
                    let w = if (payloadSpec d[ruleId]).IsSome then fst (this.Choose(m, ruleId, 0)) else d[ruleId]

                    if w.Distance = 1 then
                        List.rev (w.Edge :: acc)
                    else
                        walk w.Edge.Target (w.Edge :: acc) (guard + 1)
                | _ -> List.rev acc

        walk m [] 0

    /// The payload of the primitive `m` reaches for `ruleId`, in terms of `m`'s arguments, worked out backwards
    /// along the witness path (docs/library-payloads-plan.md). None when the rule has no payload.
    member this.PayloadOf(m: MethodRef, ruleId: string) : PayloadOrigin option =
        match best.TryGetValue m.Key with
        | true, d when d.ContainsKey ruleId ->
            payloadSpec d[ruleId]
            |> Option.map (fun spec -> { Role = spec.Role; Origin = snd (this.Choose(m, ruleId, 0)) })
        | _ -> None

    /// For a rule with a payload: the witness to use for `m` and the payload's origin along it. The candidates
    /// are `m`'s calls that reach the rule (the primitive itself, or a method that reaches it), shortest first; the
    /// first whose origin is not entirely unknown wins, else the shortest witness. A dependency often reaches a
    /// primitive by several routes (Dapper's multi-row loop is shorter than its single-command path).
    member private this.Choose(m: MethodRef, ruleId: string, depth: int) : Witness * Values.Origin =
        let key = struct (m.Key, ruleId)
        let shortest = best[m.Key][ruleId]

        match chosen.TryGetValue key with
        | true, c -> c
        | _ when depth > 32 -> shortest, Values.Origin.unknown
        | _ ->
            // The shortest and unknown while computed, so recursion through the same method ends.
            chosen[key] <- (shortest, Values.Origin.unknown)
            let spec = (payloadSpec shortest).Value

            let candidates =
                graph.Callees m
                |> List.filter followed
                |> List.choose (fun e ->
                    match sinkOf set matcher e with
                    | Some hit when hit.Rule.Id = ruleId -> Some { Distance = 1; Edge = e; Hit = hit }
                    | Some _ -> None
                    | None ->
                        match best.TryGetValue e.Target.Key with
                        | true, d when d.ContainsKey ruleId ->
                            Some
                                { Distance = d[ruleId].Distance + 1
                                  Edge = e
                                  Hit = d[ruleId].Hit }
                        | _ -> None)
                |> List.sortBy (fun w -> w.Distance, w.Edge.Offset)
                |> List.truncate 8

            let originVia (w: Witness) : Values.Origin =
                match flowOf m with
                | None -> Values.Origin.unknown
                | Some flow ->
                    let evaluator =
                        Values.Evaluator(flow, recognizers, Values.constantReturn set, fun m n -> producers.Member(m, n))

                    let isNewObj =
                        match flow.InstructionAt w.Edge.Offset with
                        | Some { Op = NewObj _ } -> true
                        | _ -> false

                    if w.Distance = 1 then
                        // The call to the primitive: its payload operand, or a property of its receiver.
                        match spec.Property with
                        | Some property when spec.Argument >= 0 ->
                            Values.Origin.ofSymbolic (
                                evaluator.OperandMember(
                                    w.Edge.Offset,
                                    Values.operandIndex w.Edge.Declared isNewObj spec.Argument,
                                    property,
                                    spec.ConstructorArgument
                                )
                            )
                        | Some property when w.Edge.Declared.HasThis && not isNewObj ->
                            Values.Origin.ofSymbolic (evaluator.ReceiverMember(w.Edge.Offset, property, spec.ConstructorArgument))
                        | Some _ -> Values.Origin.unknown
                        | None ->
                            match List.tryItem (Values.operandIndex w.Edge.Declared isNewObj spec.Argument) (flow.OperandsAt w.Edge.Offset) with
                            | Some v -> evaluator.OriginOf v
                            | None -> Values.Origin.unknown
                    else
                        let callee () = snd (this.Choose(w.Edge.Target, ruleId, depth + 1))

                        match w.Edge.Kind with
                        | Direct
                        | Virtual -> evaluator.Substitute(callee (), w.Edge.Offset, isNewObj)
                        | StateMachine
                        | Deferred when w.Edge.Target.HasThis ->
                            // The body runs later, on an object this method makes: an async method's or iterator's
                            // state machine, or a function object (an F# closure or sequence expression).
                            evaluator.ThroughStateMachine(
                                callee (),
                                w.Edge.Offset,
                                w.Edge.Target.Type,
                                (fun field -> recognizers |> List.collect (fun r -> r.StateMachineArgumentFields field)),
                                (w.Edge.Kind = Deferred)
                            )
                        | _ -> Values.Origin.unknown

            let known (o: Values.Origin) = o |> List.exists (fun s -> s <> Values.Unknown)

            let result =
                candidates
                |> Seq.map (fun w -> w, originVia w)
                |> Seq.tryFind (snd >> known)
                |> Option.defaultWith (fun () ->
                    // Nothing known on any route: the shortest witness, with its origin as far as it goes.
                    shortest, (if candidates |> List.contains shortest then originVia shortest else Values.Origin.unknown))

            // Through a callee, the primitive is the one at the end of the callee's chosen route: a rule covers
            // several members, and that route need not end where the callee's shortest one does.
            let result =
                match result with
                | w, origin when w.Distance > 1 && best.ContainsKey w.Edge.Target.Key ->
                    { w with Hit = (fst (this.Choose(w.Edge.Target, ruleId, depth + 1))).Hit }, origin
                | r -> r

            chosen[key] <- result
            result

    /// Every rule the method can reach, each with its witness path as steps.
    member this.Effects(m: MethodRef) : DependencyEffect list =
        this.Reaches m
        |> List.map (fun w ->
            { RuleId = w.Hit.Rule.Id
              Category = w.Hit.Category
              Primitive = w.Hit.Primitive.Display
              PrimitiveAssembly = w.Hit.Primitive.Type.Assembly
              Steps = this.Path(m, w.Hit.Rule.Id) |> List.map toStep
              Payload = this.PayloadOf(m, w.Hit.Rule.Id) })
