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

/// A rule a dependency method can reach, with the shortest path to it.
type DependencyEffect =
    { RuleId: string
      Category: EffectCategory
      Primitive: string
      PrimitiveAssembly: string
      Steps: PathStep list }

let toStep (e: CallEdge) =
    { Caller = e.Caller.Display
      CallerAssembly = e.Caller.Type.Assembly
      Declared = e.Declared.Display
      Virtual = e.Kind = Virtual }

type Summaries(graph: CallGraph, matcher: Matcher, isSummarized: MethodRef -> bool) =
    let set = graph.Set
    let best = Dictionary<string, Dictionary<string, Witness>>()

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
            for e in graph.Callees m |> List.filter (fun e -> e.Kind <> Initializer) do
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

    /// Every rule the method can reach, with the shortest witness for each.
    member _.Reaches(m: MethodRef) : Witness list =
        match best.TryGetValue m.Key with
        | true, d -> d.Values |> Seq.sortBy (fun w -> w.Distance, w.Hit.Rule.Id) |> Seq.toList
        | _ -> []

    /// The call path from `m` to the primitive of `ruleId`: each edge in order, the last one being the sink call.
    member this.Path(m: MethodRef, ruleId: string) : CallEdge list =
        let rec walk (m: MethodRef) (acc: CallEdge list) (guard: int) =
            if guard > 64 then
                List.rev acc
            else
                match best.TryGetValue m.Key with
                | true, d when d.ContainsKey ruleId ->
                    let w = d[ruleId]

                    if w.Distance = 1 then
                        List.rev (w.Edge :: acc)
                    else
                        walk w.Edge.Target (w.Edge :: acc) (guard + 1)
                | _ -> List.rev acc

        walk m [] 0

    /// Every rule the method can reach, each with its shortest path as steps.
    member this.Effects(m: MethodRef) : DependencyEffect list =
        this.Reaches m
        |> List.map (fun w ->
            { RuleId = w.Hit.Rule.Id
              Category = w.Hit.Category
              Primitive = w.Hit.Primitive.Display
              PrimitiveAssembly = w.Hit.Primitive.Type.Assembly
              Steps = this.Path(m, w.Hit.Rule.Id) |> List.map toStep })
