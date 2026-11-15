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
    /// The request path split into its segments (an array or a list).
    | Segments
    /// One segment of the path, by 0-based index.
    | Segment of int
    /// The segments from this index on (what is left after skipping that many).
    | SegmentsFrom of int
    /// The number of path segments.
    | SegmentCount

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

let private constInt (flow: Flow) (values: Values) =
    match Set.toList values with
    | [ Pushed o ] ->
        match flow.InstructionAt o with
        | Some { Op = LdcI4 n } -> Some n
        | _ -> None
    | _ -> None

/// What a call outside user code does to the tags of its operands: list and array access move between
/// segment tags; anything else keeps every tag (plan section 6.3: tags survive transformations).
/// `constants` gives the integer constant at each operand, where known (for indexing).
let transferValues (recognizers: ICodegenRecognizer list) (m: MethodRef) (operandTags: Tags list) (constants: int option list) : Tags =
    let all = Set.unionMany operandTags

    let access =
        match recognizers |> List.tryPick (fun r -> r.CollectionAccess m) with
        | Some a -> Some a
        | None ->
            match m.Type.FullName, m.Name with
            | "System.Linq.Enumerable", ("ToList" | "ToArray") -> Some SameElements
            | "System.Linq.Enumerable", ("Count" | "LongCount") when m.ParameterCount = 1 -> Some Count
            | ("System.Collections.Generic.List`1" | "System.Collections.Generic.IList`1" | "System.Collections.Generic.ICollection`1" | "System.Collections.Generic.IReadOnlyCollection`1"), "get_Count" -> Some Count
            | "System.Array", "get_Length" -> Some Count
            | _ -> None

    let receiver = match operandTags with first :: _ -> first | [] -> Set.empty

    let mapped =
        match access with
        | Some Head ->
            receiver
            |> Set.toList
            |> List.choose (function
                | Segments -> Some(Segment 0)
                | SegmentsFrom n -> Some(Segment n)
                | _ -> None)
        | Some Tail ->
            receiver
            |> Set.toList
            |> List.choose (function
                | Segments -> Some(SegmentsFrom 1)
                | SegmentsFrom n -> Some(SegmentsFrom(n + 1))
                | _ -> None)
        | Some Count ->
            if receiver.Contains Segments || receiver.Contains(SegmentsFrom 0) then [ SegmentCount ] else []
        | Some SameElements -> []
        | None ->
            match m.Type.FullName, m.Name with
            | "System.String", "Split" when receiver.Contains(Part RequestPart.Path) -> [ Segments ]
            | ("System.Collections.Generic.List`1" | "System.Collections.Generic.IList`1" | "System.Collections.Generic.IReadOnlyList`1"), "get_Item"
            | "System.Linq.Enumerable", "ElementAt" when receiver.Contains Segments ->
                match List.tryItem 1 constants |> Option.flatten with
                | Some i -> [ Segment i ]
                | None -> []
            | _ -> []

    match access with
    | Some Head
    | Some Tail
    | Some Count -> Set.ofList mapped |> Set.union (all |> Set.filter (function Part _ | HttpContext | Connection -> true | _ -> false))
    | _ -> Set.union all (Set.ofList mapped)

let transfer (recognizers: ICodegenRecognizer list) (flow: Flow) (m: MethodRef) (operands: Values list) (tagsOf: Values -> Tags) : Tags =
    transferValues recognizers m (operands |> List.map tagsOf) (operands |> List.map (constInt flow))

/// Runs the analysis over the given user methods.
let analyze (set: AssemblySet) (graph: CallGraph) (matcher: Matcher) (recognizers: ICodegenRecognizer list) (methods: MethodDef list) : TaintResult =
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

    // Results of tag queries, valid while the facts above do not change: cleared whenever they might.
    let memo = Dictionary<struct (string * int), Tags>()

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
        let key = struct (m.Ref.Key, offset)

        match memo.TryGetValue key with
        | true, t -> t
        | _ ->
            // Placeholder breaks cycles through merges; the real value replaces it.
            memo[key] <- Set.empty
            let t = computeResultTags m flow depth offset
            memo[key] <- t
            t

    and computeResultTags (m: MethodDef) (flow: Flow) (depth: int) (offset: int) : Tags =
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
                | LdElem ->
                    match flow.OperandsAt offset with
                    | [ array; index ] ->
                        let arrayTags = valueTags m flow (depth + 1) array

                        match constInt flow index with
                        | Some i when arrayTags.Contains Segments -> Set.add (Segment i) (arrayTags |> Set.filter (function Part _ -> true | _ -> false))
                        | _ -> Set.union arrayTags (valueTags m flow (depth + 1) index)
                    | _ -> operandTags ()
                | LdLen ->
                    let arrayTags = operandTags ()
                    if arrayTags.Contains Segments then Set.singleton SegmentCount else arrayTags
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
                            // Outside user code: tags survive the call, moved by list and array access.
                            transfer recognizers flow declared (flow.OperandsAt offset) (valueTags m flow (depth + 1))
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
        memo.Clear()

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

    memo.Clear()

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
