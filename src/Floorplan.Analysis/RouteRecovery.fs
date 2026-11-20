/// Route recovery (plan section 6.4): collects the conditions a request must satisfy to reach a handler,
/// wherever they occur and in whatever form, and assembles them into a route. Form A reads comparisons of
/// request data with constants on the way to the handler; form B reads tables of constants and handlers that
/// are consulted with request data. Nothing is guessed: what cannot be read is left out, and the result says
/// how complete it is.
module Floorplan.Analysis.RouteRecovery

open System
open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Analysis.Taint
open Floorplan.Analysis.EntryPoints

/// What a tagged value is, as the subject of a predicate.
let describe (tags: Tags) : (RequestPart * int option) option =
    let segment = tags |> Seq.tryPick (function Segment i -> Some i | _ -> None)

    match segment with
    | Some i -> Some(RequestPart.PathSegment, Some i)
    | None when tags.Contains SegmentCount -> Some(RequestPart.SegmentCount, None)
    | None ->
        [ RequestPart.Method; RequestPart.Path; RequestPart.Host; RequestPart.Query; RequestPart.Header; RequestPart.Body ]
        |> List.tryFind (fun p -> tags.Contains(Part p))
        |> Option.map (fun p -> p, None)

type private Condition =
    { WhenTrue: Predicate list
      WhenFalse: Predicate list }

let private noCondition = { WhenTrue = []; WhenFalse = [] }

type Recovery(ctx: AppContext) =
    let set = ctx.Program.Set
    let flows = Dictionary<string, Flow option>()
    let cfgs = Dictionary<string, ControlFlow.Graph>()
    let summaries = Dictionary<string, Predicate list option>()

    let flowOf (m: MethodDef) =
        match flows.TryGetValue m.Ref.Key with
        | true, f -> f
        | _ ->
            let f = DataFlow.ofMethod m
            flows[m.Ref.Key] <- f
            f

    let cfgOf (m: MethodDef) =
        match cfgs.TryGetValue m.Ref.Key with
        | true, g -> g
        | _ ->
            let g = ControlFlow.build m.Body.Value.Value
            cfgs[m.Ref.Key] <- g
            g

    let evaluator (flow: Flow) =
        Values.Evaluator(flow, ctx.Program.Recognizers, Values.constantReturn set)

    /// The constant operand `index` of the instruction at `offset`. `captured` holds constants known from the
    /// caller: those a closure was created with, by field (in the closure's own code, a read of one of its fields
    /// is that constant), and the constant arguments of a call, as `#index` (a read of that argument).
    let constantIn (captured: Map<string, string>) (flow: Flow) (offset: int) (index: int) : string option =
        match List.tryItem index (flow.OperandsAt offset) with
        | Some values ->
            match Set.toList values with
            | [ Pushed o ] ->
                match flow.InstructionAt o with
                | Some { Op = LdcI4 n } -> Some(string n)
                | Some { Op = LdcI8 n } -> Some(string n)
                | _ ->
                    match (evaluator flow).Operand(offset, index) with
                    | Values.Text t -> Some t
                    | Values.Param(0, [ field ]) -> captured.TryFind field
                    | Values.Param(i, []) -> captured.TryFind("#" + string i)
                    | _ -> None
            | [ Argument i ] -> captured.TryFind("#" + string i)
            | _ -> None
        | None -> None

    let constant = constantIn Map.empty

    let initializers = Dictionary<string, MethodDef list>()

    /// Methods of an assembly that store a static field: its initializers.
    let initializersOf (f: FieldRef) =
        match initializers.TryGetValue f.Key with
        | true, ms -> ms
        | _ ->
            let ms =
                match set.TryLoad f.Type.Assembly with
                | Some a ->
                    a.Types
                    |> List.collect (fun t -> t.Methods)
                    |> List.filter (fun m ->
                        match m.Body.Value with
                        | Some b -> b.Instructions |> Array.exists (fun i -> i.Op = StSFld f)
                        | None -> false)
                | None -> []

            initializers[f.Key] <- ms
            ms

    /// The closure a function value is, with the constants it captured, by field: a closure created here, or one
    /// held in a static field, as F# keeps a module-level function value such as `let (|GET|_|) = matcher "GET"`
    /// (read through its property getter).
    let rec closureOf (flow: Flow) (values: Values) (depth: int) : (TypeRef * Map<string, string>) option =
        match Set.toList values with
        | [ Pushed o ] when depth < 4 ->
            match flow.InstructionAt o with
            | Some { Op = NewObj ctor } when set.IsUser ctor.Type.Assembly ->
                // The constructor stores its arguments into the closure's fields.
                let captured =
                    match set.TryMethod ctor |> Option.bind (fun (def, _) -> DataFlow.ofMethod def) with
                    | Some ctorFlow ->
                        ctorFlow.Body.Instructions
                        |> Array.choose (fun ins ->
                            match ins.Op, ctorFlow.OperandsAt ins.Offset with
                            | StFld field, [ target; stored ] when target = Set.singleton (Argument 0) ->
                                match Set.toList stored with
                                | [ Argument i ] when i > 0 ->
                                    constant flow o (i - 1) |> Option.map (fun c -> Values.memberOfField field.Name, c)
                                | _ -> None
                            | _ -> None)
                        |> Map.ofArray
                    | None -> Map.empty

                Some(ctor.Type, captured)
            | Some { Op = LdSFld f } when set.IsUser f.Type.Assembly -> staticClosure f depth
            | Some { Op = Call getter } when getter.ParameterCount = 0 && not getter.HasThis && set.IsUser getter.Type.Assembly ->
                match set.TryMethod getter with
                | Some(def, _) ->
                    match def.Body.Value |> Option.map (fun b -> b.Instructions |> Array.filter (fun i -> i.Op <> Nop)) with
                    | Some [| { Op = LdSFld f }; { Op = Ret } |] -> staticClosure f depth
                    | _ -> None
                | None -> None
            | _ -> None
        | _ -> None

    and staticClosure (f: FieldRef) (depth: int) =
        // Only a field every initializer sets to the same closure.
        let found =
            initializersOf f
            |> List.collect (fun init ->
                match DataFlow.ofMethod init with
                | Some initFlow ->
                    initFlow.Body.Instructions
                    |> Array.filter (fun i -> i.Op = StSFld f)
                    |> Array.toList
                    |> List.map (fun i ->
                        match initFlow.OperandsAt i.Offset with
                        | [ v ] -> closureOf initFlow v (depth + 1)
                        | _ -> None)
                | None -> [ None ])
            |> List.distinct

        match found with
        | [ Some c ] -> Some c
        | _ -> None

    /// The Invoke method a closure of type `closure` runs for a call to `invoke`.
    let invokeOf (closure: TypeRef) (invoke: MethodRef) =
        match set.TryType closure with
        | Some(def, _) ->
            def.Methods
            |> List.tryFind (fun m ->
                m.Ref.Name = "Invoke" && not m.IsAbstract && m.Ref.ParameterCount = invoke.ParameterCount && m.Body.Value.IsSome)
        | None -> None

    let isFunctionInvoke (m: MethodRef) =
        m.Name = "Invoke" && m.HasThis && m.Type.FullName.StartsWith "Microsoft.FSharp.Core.FSharpFunc`"

    let predicate (location: Location option) (part: RequestPart, index: int option) (op: PredicateOperator) (value: string) =
        { Part = part
          Operator = op
          Index = index
          Value = value
          Location = location }

    let isEquality (m: MethodRef) =
        ctx.Program.Recognizers |> List.exists (fun r -> r.IsStringEquality m)
        || (m.Type.FullName = "System.String" && (m.Name = "Equals" || m.Name = "op_Equality"))
        || (m.Type.FullName = "Microsoft.AspNetCore.Http.PathString" && (m.Name = "Equals" || m.Name = "op_Equality"))

    /// A comparison between two operands of the instruction at `offset`: one request value, one constant.
    let comparison (captured: Map<string, string>) (m: MethodDef) (flow: Flow) (offset: int) (left: int) (right: int) (op: PredicateOperator) (location: Location option) =
        let tags = ctx.Taint.OperandTags m.Ref offset

        let attempt (tagged: int) (literal: int) =
            match List.tryItem tagged tags |> Option.bind describe, constantIn captured flow offset literal with
            | Some subject, Some value -> Some(predicate location subject op value)
            | _ -> None

        match attempt left right with
        | Some p -> [ p ]
        | None -> attempt right left |> Option.toList

    let rec conditionOf (captured: Map<string, string>) (m: MethodDef) (flow: Flow) (depth: int) (values: Values) : Condition =
        match Set.toList values with
        | [ Pushed offset ] when depth < 6 ->
            match flow.InstructionAt offset with
            | None -> noCondition
            | Some ins ->
                let location =
                    Instruction.pointAt flow.Body.Instructions (Instruction.indexOfOffset flow.Body.Instructions offset)
                    |> Option.map Attribution.toLocation

                match ins.Op with
                | Call x
                | CallVirt x when (ctx.Matcher.Comparison x).IsSome ->
                    // A platform comparison with a known value, e.g. HttpMethods.IsPatch(method).
                    let rule = (ctx.Matcher.Comparison x).Value
                    let tags = ctx.Taint.OperandTags m.Ref offset |> List.tryHead |> Option.defaultValue Set.empty

                    match describe tags, rule.RequestPart, rule.ComparesTo with
                    | Some _, Some part, Some value -> { noCondition with WhenTrue = [ predicate location (part, None) PredicateOperator.Equals value ] }
                    | _ -> noCondition
                | Call x
                | CallVirt x when isEquality x && (flow.OperandsAt offset).Length >= 2 ->
                    { noCondition with WhenTrue = comparison captured m flow offset 0 1 PredicateOperator.Equals location }
                | Call x
                | CallVirt x when x.Type.FullName = "System.String" && (x.Name = "StartsWith" || x.Name = "EndsWith" || x.Name = "Contains") ->
                    let op =
                        match x.Name with
                        | "StartsWith" -> PredicateOperator.StartsWith
                        | "EndsWith" -> PredicateOperator.EndsWith
                        | _ -> PredicateOperator.Contains

                    { noCondition with WhenTrue = comparison captured m flow offset 0 1 op location }
                | Call x
                | CallVirt x when x.Type.FullName = "Microsoft.AspNetCore.Http.PathString" && x.Name = "StartsWithSegments" ->
                    { noCondition with WhenTrue = comparison captured m flow offset 0 1 PredicateOperator.StartsWith location }
                | Call x when x.Type.FullName = "System.Text.RegularExpressions.Regex" && x.Name = "IsMatch" && not x.HasThis ->
                    { noCondition with WhenTrue = comparison captured m flow offset 0 1 PredicateOperator.Matches location }
                | CallVirt x
                | Call x when x.Type.FullName = "System.Text.RegularExpressions.Regex" && x.Name = "IsMatch" && x.HasThis ->
                    // new Regex(pattern).IsMatch(input): the pattern is the constructor's argument.
                    let pattern =
                        match flow.OperandsAt offset with
                        | regex :: _ ->
                            match Set.toList regex with
                            | [ Pushed o ] ->
                                match flow.InstructionAt o with
                                | Some { Op = NewObj ctor } when ctor.Type.FullName = "System.Text.RegularExpressions.Regex" -> constant flow o 0
                                | _ -> None
                            | _ -> None
                        | [] -> None

                    let subject = ctx.Taint.OperandTags m.Ref offset |> List.tryItem 1 |> Option.bind describe

                    match pattern, subject with
                    | Some p, Some s -> { noCondition with WhenTrue = [ predicate location s PredicateOperator.Matches p ] }
                    | _ -> noCondition
                | CallVirt x when isFunctionInvoke x ->
                    // A function value applied, such as an active pattern made by partial application: the
                    // conditions under which the closure it holds succeeds, with the constants it captured.
                    let closure =
                        flow.OperandsAt offset
                        |> List.tryHead
                        |> Option.bind (fun receiver -> closureOf flow receiver 0)
                        |> Option.bind (fun (t, captured) -> invokeOf t x |> Option.map (fun invoke -> invoke, captured))

                    match closure |> Option.bind (fun (invoke, inner) -> successOf invoke inner (depth + 1)) with
                    | Some ps -> { noCondition with WhenTrue = ps }
                    | None -> noCondition
                | Call x
                | CallVirt x when ctx.Methods.ContainsKey x.Key ->
                    // A helper returning bool or an option (an active pattern): its own conditions for success,
                    // with the constants it is called with (the method a closure passes on to a shared matcher).
                    let arguments =
                        flow.OperandsAt offset
                        |> List.indexed
                        |> List.choose (fun (j, _) -> constantIn captured flow offset j |> Option.map (fun c -> "#" + string j, c))
                        |> Map.ofList

                    match successOf ctx.Methods[x.Key] arguments (depth + 1) with
                    | Some ps -> { noCondition with WhenTrue = ps }
                    | None -> noCondition
                | Ceq -> { noCondition with WhenTrue = comparison captured m flow offset 0 1 PredicateOperator.Equals location }
                | _ ->
                    // A list's tail is null exactly when the list has ended: the segment count is known.
                    let tags = ctx.Taint.ResultTags m.Ref offset

                    match tags |> Seq.tryPick (function SegmentsFrom n -> Some n | _ -> None) with
                    | Some n ->
                        { noCondition with
                            WhenFalse = [ predicate location (RequestPart.SegmentCount, None) PredicateOperator.Equals (string (n - 1)) ] }
                    | None -> noCondition
        | many when many.Length > 1 && depth < 6 ->
            // A boolean merged from several paths, as `a && b` and `a || b` compile: on the true side the
            // constant-false producers cannot have produced it, so the others did, under their own conditions.
            let constantOf (v: Value) =
                match v with
                | Pushed o ->
                    match flow.InstructionAt o with
                    | Some { Op = LdcI4 n } -> Some n
                    | _ -> None
                | _ -> None

            let side (impossible: int) (pick: Condition -> Predicate list) =
                match many |> List.filter (fun v -> constantOf v <> Some impossible) with
                | [ Pushed o as only ] ->
                    let index = Instruction.indexOfOffset flow.Body.Instructions o
                    let own = if constantOf only = Some(1 - impossible) then [] else pick (conditionOf captured m flow (depth + 1) (Set.singleton only))
                    own @ (if index >= 0 then pathPredicatesIn captured m index else [])
                | _ -> []

            { WhenTrue = side 0 (fun c -> c.WhenTrue)
              WhenFalse = side 1 (fun c -> c.WhenFalse) }
        | _ -> noCondition

    /// Predicates implied by taking successor `direction` of the conditional branch at `index`.
    and branchPredicates (captured: Map<string, string>) (m: MethodDef) (flow: Flow) (index: int) (direction: int) : Predicate list =
        let ins = flow.Body.Instructions[index]
        let operands = flow.OperandsAt ins.Offset

        let location = Instruction.pointAt flow.Body.Instructions index |> Option.map Attribution.toLocation

        match ins.Op, operands with
        | BrTrue _, [ v ] ->
            let c = conditionOf captured m flow 0 v
            if direction = 1 then c.WhenTrue else c.WhenFalse
        | BrFalse _, [ v ] ->
            let c = conditionOf captured m flow 0 v
            if direction = 1 then c.WhenFalse else c.WhenTrue
        | BrCmp(Eq, _), [ _; _ ] -> if direction = 1 then comparison captured m flow ins.Offset 0 1 PredicateOperator.Equals location else []
        | BrCmp(NotEq, _), [ _; _ ] -> if direction = 0 then comparison captured m flow ins.Offset 0 1 PredicateOperator.Equals location else []
        | _ -> []

    /// Predicates that hold whenever the instruction at `target` runs: one per request-data branch deciding it.
    and pathPredicatesIn (captured: Map<string, string>) (m: MethodDef) (target: int) : Predicate list =
        match flowOf m with
        | None -> []
        | Some flow ->
            let cfg = cfgOf m

            taintedBranches ctx m
            |> List.collect (fun (b, _) ->
                if ControlFlow.isControlledBy cfg b target then
                    match ControlFlow.directionTo cfg b target with
                    | Some d -> branchPredicates captured m flow b d
                    | None -> []
                else
                    [])
            |> List.distinct

    /// The conditions under which a helper returns true or a value (not null), common to all its successes.
    and successOf (m: MethodDef) (captured: Map<string, string>) (depth: int) : Predicate list option =
        let key =
            if captured.IsEmpty then m.Ref.Key
            else m.Ref.Key + "|" + (captured |> Map.toList |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ",")

        match summaries.TryGetValue key with
        | true, s -> s
        | _ when depth > 4 -> None
        | _ ->
            summaries[key] <- None

            let result =
                match flowOf m with
                | None -> None
                | Some flow ->
                    let instrs = flow.Body.Instructions

                    let successes =
                        instrs
                        |> Array.mapi (fun i ins -> i, ins)
                        |> Array.choose (fun (i, ins) ->
                            match ins.Op, flow.OperandsAt ins.Offset with
                            | Ret, [ v ] ->
                                let failure =
                                    match Set.toList v with
                                    | [ Pushed o ] ->
                                        match flow.InstructionAt o with
                                        | Some { Op = (LdNull | LdcI4 0) } -> true
                                        | _ -> false
                                    | _ -> false

                                if failure then
                                    None
                                else
                                    let returned = (conditionOf captured m flow (depth + 1) v).WhenTrue
                                    Some(Set.ofList (pathPredicatesIn captured m i @ returned))
                            | _ -> None)
                        |> Array.toList

                    match successes with
                    | [] -> None
                    | first :: rest -> Some(rest |> List.fold Set.intersect first |> Set.toList)

            summaries[key] <- result
            result

    let pathPredicates (m: MethodDef) (target: int) = pathPredicatesIn Map.empty m target

    /// Callers of a method within the application, with the call instruction index.
    let callSites (m: MethodRef) =
        ctx.Methods.Values
        |> Seq.collect (fun c ->
            ctx.Program.Graph.Callees c.Ref
            |> List.filter (fun e -> e.Target.Key = m.Key && (e.Kind = Direct || e.Kind = Virtual || e.Kind = Deferred))
            |> List.map (fun e -> c, Instruction.indexOfOffset c.Body.Value.Value.Instructions e.Offset))
        |> Seq.filter (fun (_, i) -> i >= 0)
        |> Seq.toList

    /// Predicates from the callers of `m`, up to the root: one list per call path.
    let rec upward (m: MethodDef) (depth: int) (seen: Set<string>) : Predicate list list =
        let sites = if depth > 4 || seen.Contains m.Ref.Key then [] else callSites m.Ref

        if sites.IsEmpty then
            [ [] ]
        else
            sites
            |> List.collect (fun (caller, index) ->
                let here = pathPredicates caller index
                upward caller (depth + 1) (seen.Add m.Ref.Key) |> List.map (fun up -> here @ up))
            |> List.map List.distinct
            |> List.distinct
            |> List.truncate 6

    member _.PathPredicates(m, index) = pathPredicates m index
    member _.Upward(m, depth) = upward m depth Set.empty

    /// Route parameters: segments passed to the handler without being compared.
    member _.Parameters(m: MethodDef, index: int, compared: Predicate list) : Predicate list =
        let ins = m.Body.Value.Value.Instructions[index]
        let location = Instruction.pointAt m.Body.Value.Value.Instructions index |> Option.map Attribution.toLocation

        let comparedSegments =
            compared |> List.choose (fun p -> if p.Part = RequestPart.PathSegment then p.Index else None) |> Set.ofList

        ctx.Taint.OperandTags m.Ref ins.Offset
        |> Set.unionMany
        |> Seq.choose (function Segment i when not (comparedSegments.Contains i) -> Some i | _ -> None)
        |> Seq.distinct
        |> Seq.map (fun i -> predicate location (RequestPart.PathSegment, Some i) PredicateOperator.Parameter "{param}")
        |> Seq.toList

    member _.Flow(m) = flowOf m
    member _.Constant(flow, offset, index) = constant flow offset index

/// A route template from an anchored path pattern made of literal text and capture groups, such as
/// ^/api/orders/(-?\d+)$ (a format-string route compiled to a regex): each group is a parameter. Anything else is not a template.
let templateOfPattern (pattern: string) : string option =
    if pattern.Length < 3 || pattern[0] <> '^' || pattern[pattern.Length - 1] <> '$' then
        None
    else
        let body = pattern.Substring(1, pattern.Length - 2)
        let text = Text.StringBuilder()
        let mutable groups = 0
        let mutable ok = true
        let mutable i = 0

        while ok && i < body.Length do
            match body[i] with
            | '\\' when i + 1 < body.Length && not (Char.IsLetterOrDigit body[i + 1]) ->
                text.Append(body[i + 1]) |> ignore
                i <- i + 2
            | '(' when i + 1 < body.Length && body[i + 1] <> '?' ->
                // One group, not nested, that cannot run over a segment boundary unseen.
                match body.IndexOf(')', i) with
                | close when close > i && not (body.Substring(i + 1, close - i - 1).Contains '(') ->
                    groups <- groups + 1
                    text.Append("{param").Append(groups).Append('}') |> ignore
                    i <- close + 1
                | _ -> ok <- false
            | c when "\\.*+?()[]{}|^$".IndexOf c >= 0 -> ok <- false
            | c ->
                text.Append c |> ignore
                i <- i + 1

        if not ok || text.Length = 0 || text[0] <> '/' then None
        elif groups = 1 then Some(text.ToString().Replace("{param1}", "{param}"))
        else Some(text.ToString())

/// Names the parameters of a route made from a pattern ({param}, {param1}, ...) after the handler's own
/// parameters, in order, when there are exactly as many names as placeholders. Otherwise the route is unchanged.
let nameParameters (names: string list) (route: string) =
    let placeholders = Text.RegularExpressions.Regex.Matches(route, @"\{param\d*\}")

    if placeholders.Count = 0
       || placeholders.Count <> names.Length
       || names |> List.exists (fun n -> String.IsNullOrEmpty n || not (Char.IsLetter n[0]) || n.Contains '@') then
        route
    else
        let mutable i = -1

        Text.RegularExpressions.Regex.Replace(
            route,
            @"\{param\d*\}",
            fun _ ->
                i <- i + 1
                "{" + names[i] + "}"
        )

/// Values taken from the path are passed to the handler: its parameters name the route's placeholders, when there
/// are as many as placeholders. Parameters of the request pipeline itself (the context, the next function) are not
/// route values.
let nameFromHandler (set: AssemblySet) (handler: MethodRef) (route: string) =
    match set.TryMethod handler with
    | Some(def, _) when def.ParameterNames.Length = handler.ParameterTypes.Length ->
        let names =
            List.zip handler.ParameterTypes def.ParameterNames
            |> List.filter (fun (t, _) -> not (t.Contains "Microsoft.AspNetCore." || t.StartsWith "Microsoft.FSharp.Core.FSharpFunc"))
            |> List.map snd

        nameParameters names route
    | _ -> route

/// Turns predicates into a method, a route and a completeness level (plan section 6.6).
let assemble (predicates: Predicate list) : string option * string option * Completeness =
    let methods =
        predicates
        |> List.filter (fun p -> p.Part = RequestPart.Method && p.Operator = PredicateOperator.Equals)
        |> List.map (fun p -> p.Value.ToUpperInvariant())
        |> List.distinct

    let httpMethod = match methods with [ m ] -> Some m | _ -> None

    let fullPath =
        predicates
        |> List.tryFind (fun p -> p.Part = RequestPart.Path && p.Operator = PredicateOperator.Equals)
        |> Option.map (fun p -> p.Value)
        |> Option.orElse (
            predicates
            |> List.tryPick (fun p ->
                if p.Part = RequestPart.Path && p.Operator = PredicateOperator.Matches then templateOfPattern p.Value else None))

    let segments =
        predicates
        |> List.filter (fun p -> p.Part = RequestPart.PathSegment && p.Index.IsSome)
        |> List.groupBy (fun p -> p.Index.Value)
        |> List.choose (fun (i, ps) ->
            match ps |> List.tryFind (fun p -> p.Operator = PredicateOperator.Equals) with
            | Some p -> Some(i, p.Value)
            | None -> ps |> List.tryFind (fun p -> p.Operator = PredicateOperator.Parameter) |> Option.map (fun p -> i, p.Value))
        |> Map.ofList

    let count =
        predicates
        |> List.tryPick (fun p ->
            if p.Part = RequestPart.SegmentCount && p.Operator = PredicateOperator.Equals then
                match Int32.TryParse p.Value with
                | true, n -> Some n
                | _ -> None
            else
                None)

    let contiguous = Seq.initInfinite id |> Seq.takeWhile segments.ContainsKey |> Seq.map (fun i -> segments[i]) |> Seq.toList

    let prefix =
        predicates
        |> List.tryFind (fun p -> p.Part = RequestPart.Path && p.Operator = PredicateOperator.StartsWith)
        |> Option.map (fun p -> p.Value)

    let route, pathComplete =
        match fullPath with
        | Some p -> Some p, true
        | None ->
            match count with
            | Some n when contiguous.Length = n && segments.Count = n -> Some("/" + String.concat "/" contiguous), true
            | _ when not contiguous.IsEmpty -> Some("/" + String.concat "/" contiguous + "/*"), false
            | _ ->
                match prefix with
                | Some p -> Some(p.TrimEnd('/') + "/*"), false
                | None -> None, false

    let completeness =
        if httpMethod.IsSome && pathComplete then Completeness.Complete
        elif predicates.IsEmpty then Completeness.Unresolved
        else Completeness.Partial

    httpMethod, route, completeness

/// Lookup calls: a collection consulted with a key.
let private lookupKey (m: MethodRef) : int option =
    match m.Type.FullName, m.Name with
    | ("System.Collections.Generic.Dictionary`2" | "System.Collections.Generic.IDictionary`2" | "System.Collections.Generic.IReadOnlyDictionary`2" | "System.Collections.Concurrent.ConcurrentDictionary`2"),
      ("TryGetValue" | "get_Item" | "ContainsKey" | "GetValueOrDefault") -> Some 1
    | "System.Collections.Generic.CollectionExtensions", "GetValueOrDefault" -> Some 1
    | "Microsoft.FSharp.Collections.MapModule", ("TryFind" | "Find" | "ContainsKey") -> Some 0
    | "Microsoft.FSharp.Collections.FSharpMap`2", ("TryFind" | "get_Item" | "ContainsKey" | "TryGetValue") -> Some 1
    | _ -> None

/// Additions to a collection under a key.
let private isStore (m: MethodRef) =
    match m.Type.FullName, m.Name with
    | ("System.Collections.Generic.Dictionary`2" | "System.Collections.Generic.IDictionary`2" | "System.Collections.Concurrent.ConcurrentDictionary`2"),
      ("set_Item" | "Add" | "TryAdd") -> true
    | ("Microsoft.FSharp.Collections.FSharpMap`2"), "Add" -> true
    | _ -> false

/// Where a collection value comes from: a static or instance field, possibly behind a parameterless getter.
let rec private tableIdentity (set: AssemblySet) (flow: Flow) (values: Values) (depth: int) : string option =
    match Set.toList values with
    | [ Pushed o ] when depth < 4 ->
        match flow.InstructionAt o with
        | Some { Op = (LdSFld f | LdFld f) } -> Some f.Key
        | Some { Op = (Call g | CallVirt g) } when g.ParameterCount = 0 ->
            match set.TryMethod g with
            | Some(def, _) ->
                match def.Body.Value with
                | Some body ->
                    body.Instructions
                    |> Array.tryPick (fun i ->
                        match i.Op with
                        | LdSFld f
                        | LdFld f -> Some f.Key
                        | _ -> None)
                | None -> None
            | None -> None
        | _ -> None
    | _ -> None

/// Form B: handlers stored in a table next to constant keys, the table being consulted with request data.
let tableEntries (ctx: AppContext) (recovery: Recovery) : (MethodRef * Predicate list * EvidenceStep list) list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key
    let methods = List.ofSeq ctx.Methods.Values

    // Lookups keyed by request data, by table identity.
    let lookups =
        methods
        |> List.collect (fun m ->
            match recovery.Flow m with
            | None -> []
            | Some flow ->
                flow.Body.Instructions
                |> Array.mapi (fun i ins -> i, ins)
                |> Array.choose (fun (i, ins) ->
                    match ins.Op with
                    | Call x
                    | CallVirt x ->
                        match lookupKey x with
                        | Some keyIndex ->
                            let operands = flow.OperandsAt ins.Offset
                            let tags = ctx.Taint.OperandTags m.Ref ins.Offset

                            match List.tryItem keyIndex tags |> Option.bind describe with
                            | Some subject ->
                                let collectionIndex = if keyIndex = 0 then 1 else 0

                                List.tryItem collectionIndex operands
                                |> Option.bind (fun c -> tableIdentity set flow c 0)
                                |> Option.map (fun identity -> identity, (m, i, subject))
                            | None -> None
                        | None -> None
                    | _ -> None)
                |> Array.toList)
        |> List.groupBy fst
        |> List.map (fun (k, vs) -> k, vs |> List.map snd)
        |> Map.ofList

    if lookups.IsEmpty then
        []
    else
        // Methods that store into each table's field build it; read their constant-keyed handler entries.
        methods
        |> List.collect (fun m ->
            match recovery.Flow m with
            | None -> []
            | Some flow ->
                let storedFields =
                    flow.Body.Instructions
                    |> Array.choose (fun i ->
                        match i.Op with
                        | StSFld f
                        | StFld f when lookups.ContainsKey f.Key -> Some f.Key
                        | _ -> None)
                    |> Array.distinct

                storedFields
                |> Array.toList
                |> List.collect (fun field ->
                    let entries =
                        flow.Body.Instructions
                        |> Array.mapi (fun i ins -> i, ins)
                        |> Array.choose (fun (i, ins) ->
                            let keyAndHandler =
                                match ins.Op with
                                | Call x
                                | CallVirt x when isStore x ->
                                    match flow.OperandsAt ins.Offset with
                                    | [ _; _; handler ] -> Some(recovery.Constant(flow, ins.Offset, 1), handler)
                                    | _ -> None
                                | NewObj ctor when ctor.Type.FullName = "System.Tuple`2" ->
                                    match flow.OperandsAt ins.Offset with
                                    | [ _; handler ] -> Some(recovery.Constant(flow, ins.Offset, 0), handler)
                                    | _ -> None
                                | _ -> None

                            match keyAndHandler with
                            | Some(Some key, handler) ->
                                PlatformRouting.handlerOf set flow handler
                                |> Option.map (fun h -> PlatformRouting.unwrap set isUser 0 h, key, i)
                            | _ -> None)
                        |> Array.toList

                    entries
                    |> List.collect (fun (handler, key, storeIndex) ->
                        lookups[field]
                        |> List.collect (fun (lookupMethod, lookupIndex, subject) ->
                            let location =
                                Instruction.pointAt m.Body.Value.Value.Instructions storeIndex |> Option.map Attribution.toLocation

                            let keyPredicate =
                                { Part = fst subject
                                  Operator = PredicateOperator.Equals
                                  Index = snd subject
                                  Value = key
                                  Location = location }

                            let local = recovery.PathPredicates(lookupMethod, lookupIndex)

                            let steps =
                                [ { Description = $"{m.Ref.Display} stores {handler.Display} under \"{key}\""
                                    Member = m.Ref.Display
                                    Assembly = m.Ref.Type.Assembly
                                    Location = location }
                                  { Description = $"{lookupMethod.Ref.Display} looks the table up with request data"
                                    Member = lookupMethod.Ref.Display
                                    Assembly = lookupMethod.Ref.Type.Assembly
                                    Location =
                                      Instruction.pointAt lookupMethod.Body.Value.Value.Instructions lookupIndex
                                      |> Option.map Attribution.toLocation } ]

                            recovery.Upward(lookupMethod, 0)
                            |> List.map (fun up -> handler, (keyPredicate :: local @ up) |> List.distinct, steps)))))
