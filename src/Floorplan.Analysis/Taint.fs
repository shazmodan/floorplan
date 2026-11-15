/// Request-data tracking (plan section 6.3, step 2). Values read from inbound primitives are tagged with the
/// part of the request they carry, and the tags follow the value through locals, arguments, return values,
/// fields (so records, unions and closures), and through any call outside user code, which is assumed to
/// keep its inputs' tags in its result (splitting, trimming, lower-casing, wrapping in a list).
module Floorplan.Analysis.Taint

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog

type Tag =
    /// A part of an HTTP request.
    | Part of RequestPart
    /// A whole inbound HTTP request or context, before a part is read from it.
    | HttpContext
    /// An inbound network connection with no HTTP parsing the analysis can see.
    | Connection

type Tags = Set<Tag>

/// Where tagged data first enters user code.
type SourceSite =
    { Method: MethodRef
      Offset: int
      Rule: Rule
      Point: SourcePoint option }

type TaintResult =
    { /// Tags of each argument of each user method, by method key and argument index.
      Parameters: IReadOnlyDictionary<string, Map<int, Tags>>
      /// Tags of the value at each operand of each instruction, computed on demand.
      OperandTags: MethodRef -> int -> Tags list
      /// Tags of the value an instruction pushes.
      ResultTags: MethodRef -> int -> Tags
      Sources: SourceSite list }

let private tagOf (rule: Rule) : Tag =
    match rule.RequestPart, rule.Category with
    | Some part, _ -> Part part
    | None, Some EffectCategory.Network -> Connection
    | None, _ -> HttpContext

/// Runs the analysis over the given user methods.
let analyze (set: AssemblySet) (graph: CallGraph) (matcher: Matcher) (methods: MethodDef list) : TaintResult =
    let byKey = methods |> List.map (fun m -> m.Ref.Key, m) |> dict
    let flows = Dictionary<string, Flow option>()

    let flowOf (m: MethodDef) =
        match flows.TryGetValue m.Ref.Key with
        | true, f -> f
        | _ ->
            let f = DataFlow.ofMethod m
            flows[m.Ref.Key] <- f
            f

    let parameters = Dictionary<string, Map<int, Tags>>()
    let returns = Dictionary<string, Tags>()
    let fields = Dictionary<string, Tags>()
    let sources = ResizeArray<SourceSite>()

    // Who reads each field and who calls each method, to know what to revisit.
    let fieldReaders = Dictionary<string, HashSet<string>>()
    let callers = Dictionary<string, HashSet<string>>()

    let addTo (d: Dictionary<string, HashSet<string>>) (k: string) (v: string) =
        match d.TryGetValue k with
        | true, s -> s.Add v |> ignore
        | _ -> d[k] <- HashSet [ v ]

    for m in methods do
        match m.Body.Value with
        | Some body ->
            for ins in body.Instructions do
                match ins.Op with
                | LdFld f
                | LdSFld f
                | LdFldA f
                | LdSFldA f -> addTo fieldReaders f.Key m.Ref.Key
                | _ -> ()

            for e in graph.Callees m.Ref do
                addTo callers e.Target.Key m.Ref.Key
        | None -> ()

    let work = Queue<string>()
    let queued = HashSet<string>()

    let enqueue (key: string) =
        if byKey.ContainsKey key && queued.Add key then
            work.Enqueue key

    let targetsAt (m: MethodRef) (offset: int) (declared: MethodRef) =
        let resolved =
            graph.Callees m
            |> List.filter (fun e -> e.Offset = offset && (e.Kind = Direct || e.Kind = Virtual))
            |> List.map (fun e -> e.Target)

        if resolved.IsEmpty then [ declared ] else resolved

    let rec valueTags (m: MethodDef) (flow: Flow) (depth: int) (values: Values) : Tags =
        values |> Seq.fold (fun acc v -> Set.union acc (oneTags m flow depth v)) Set.empty

    and oneTags (m: MethodDef) (flow: Flow) (depth: int) (v: Value) : Tags =
        match v with
        | Argument i ->
            match parameters.TryGetValue m.Ref.Key with
            | true, ps -> ps.TryFind i |> Option.defaultValue Set.empty
            | _ -> Set.empty
        | CaughtException -> Set.empty
        | LocalAddress(_, offset) ->
            match flow.AddressValues.TryGetValue offset with
            | true, values when depth < 40 -> valueTags m flow (depth + 1) values
            | _ -> Set.empty
        | Pushed offset -> resultTags m flow depth offset

    and resultTags (m: MethodDef) (flow: Flow) (depth: int) (offset: int) : Tags =
        if depth > 40 then
            Set.empty
        else
            match flow.InstructionAt offset with
            | None -> Set.empty
            | Some ins ->
                let operandTags () =
                    flow.OperandsAt offset |> List.fold (fun acc vs -> Set.union acc (valueTags m flow (depth + 1) vs)) Set.empty

                match ins.Op with
                | LdArgA n -> oneTags m flow (depth + 1) (Argument n)
                | LdStr _
                | LdcI4 _
                | LdcI8 _
                | LdcR _
                | LdNull
                | LdToken
                | LdFtn _ -> Set.empty
                | LdFld f
                | LdSFld f
                | LdFldA f
                | LdSFldA f ->
                    match fields.TryGetValue f.Key with
                    | true, t -> t
                    | _ -> Set.empty
                | Call declared
                | CallVirt declared
                | NewObj declared ->
                    match matcher.Source declared with
                    | Some rule -> Set.add (tagOf rule) (operandTags ())
                    | None ->
                        let targets = targetsAt m.Ref offset declared
                        let userTargets = targets |> List.filter (fun t -> byKey.ContainsKey t.Key)

                        if userTargets.IsEmpty then
                            // Outside user code: tags survive the call.
                            operandTags ()
                        else
                            match ins.Op with
                            | NewObj _ -> operandTags ()
                            | _ ->
                                userTargets
                                |> List.fold
                                    (fun acc t ->
                                        match returns.TryGetValue t.Key with
                                        | true, r -> Set.union acc r
                                        | _ -> acc)
                                    Set.empty
                | _ -> operandTags ()

    let processMethod (m: MethodDef) =
        match flowOf m with
        | None -> ()
        | Some flow ->
            let tags (vs: Values) = valueTags m flow 0 vs

            for ins in flow.Body.Instructions do
                let operands = flow.OperandsAt ins.Offset

                match ins.Op with
                | Call declared
                | CallVirt declared
                | NewObj declared ->
                    let isNewObj = match ins.Op with NewObj _ -> true | _ -> false

                    for t in targetsAt m.Ref ins.Offset declared do
                        if byKey.ContainsKey t.Key then
                            let current = match parameters.TryGetValue t.Key with | true, p -> p | _ -> Map.empty

                            let updated =
                                operands
                                |> List.indexed
                                |> List.fold
                                    (fun (acc: Map<int, Tags>) (i, vs) ->
                                        let param = if isNewObj then i + 1 else i
                                        let ts = tags vs

                                        if ts.IsEmpty then
                                            acc
                                        else
                                            acc.Add(param, Set.union ts (acc.TryFind param |> Option.defaultValue Set.empty)))
                                    current

                            if updated <> current then
                                parameters[t.Key] <- updated
                                enqueue t.Key
                | StFld f
                | StSFld f ->
                    let value = match List.tryLast operands with Some v -> tags v | None -> Set.empty

                    if not value.IsEmpty then
                        let current = match fields.TryGetValue f.Key with | true, t -> t | _ -> Set.empty
                        let updated = Set.union current value

                        if updated <> current then
                            fields[f.Key] <- updated

                            match fieldReaders.TryGetValue f.Key with
                            | true, readers -> for r in readers do enqueue r
                            | _ -> ()
                | Ret ->
                    match operands with
                    | [ v ] ->
                        let ts = tags v

                        if not ts.IsEmpty then
                            let current = match returns.TryGetValue m.Ref.Key with | true, t -> t | _ -> Set.empty
                            let updated = Set.union current ts

                            if updated <> current then
                                returns[m.Ref.Key] <- updated

                                match callers.TryGetValue m.Ref.Key with
                                | true, cs -> for c in cs do enqueue c
                                | _ -> ()
                    | _ -> ()
                | _ -> ()

    // Seed: every method that calls a source primitive.
    for m in methods do
        match m.Body.Value with
        | Some body ->
            body.Instructions
            |> Array.iteri (fun i ins ->
                match ins.Op with
                | Call d
                | CallVirt d ->
                    match matcher.Source d with
                    | Some rule ->
                        sources.Add
                            { Method = m.Ref
                              Offset = ins.Offset
                              Rule = rule
                              Point = Instruction.pointAt body.Instructions i }

                        enqueue m.Ref.Key
                    | None -> ()
                | _ -> ())
        | None -> ()

    let mutable guard = 0

    while work.Count > 0 && guard < 100_000 do
        guard <- guard + 1
        let key = work.Dequeue()
        queued.Remove key |> ignore
        processMethod byKey[key]

    let operandTagsOf (m: MethodRef) (offset: int) =
        match byKey.TryGetValue m.Key with
        | true, def ->
            match flowOf def with
            | Some flow -> flow.OperandsAt offset |> List.map (valueTags def flow 0)
            | None -> []
        | _ -> []

    let resultTagsOf (m: MethodRef) (offset: int) =
        match byKey.TryGetValue m.Key with
        | true, def ->
            match flowOf def with
            | Some flow -> resultTags def flow 0 offset
            | None -> Set.empty
        | _ -> Set.empty

    { Parameters = parameters :> IReadOnlyDictionary<string, Map<int, Tags>>
      OperandTags = operandTagsOf
      ResultTags = resultTagsOf
      Sources = List.ofSeq sources }
