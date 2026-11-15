/// A symbolic interpreter for IL (plan section 6): it runs user code, dependency code and FSharp.Core over
/// abstract values, so that routing written inside a framework is followed like any other code.
///
/// - Objects live on a persistent heap, so paths fork cheaply. Closures, records, lists and state machines are
///   ordinary objects; a virtual call dispatches on the concrete type of its receiver, which resolves function
///   values precisely.
/// - Request data is tagged (Taint.Tag). A branch on request data forks the path and records the predicate.
/// - Shared-framework calls are not interpreted; they are modelled from the boundary catalog (sources,
///   registrations, comparisons, callbacks) plus a few platform conventions (async builders start their state
///   machine; awaits complete; pure string functions fold on constants).
///
/// Everything is bounded (steps, depth, threads, loop visits); running out of budget loses paths, never
/// invents them.
module Floorplan.Analysis.Symbolic

open System
open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog
open Floorplan.Analysis.Taint

type Condition =
    { WhenTrue: Predicate list
      WhenFalse: Predicate list
      /// A comparison of a value the analysis does not know with a constant: taking its true side means the
      /// request satisfies a condition that cannot be stated, so no route recovered on that path is trusted.
      Uncertain: bool }

type SVal =
    /// A string, an integer (as int64) or a float.
    | Const of obj
    | Null
    | Ref of int
    | Req of Tags
    /// A method pointer or delegate, with its target object.
    | Fn of MethodRef * SVal option
    /// A boolean that is true exactly when some request predicates hold.
    | Bool of Condition
    /// The address of a field of a heap object (a by-ref argument such as `&enumerator.current`).
    | FieldAddress of objectId: int * field: string
    | Unknown

type HeapObj =
    { Type: TypeRef
      Fields: Map<string, SVal>
      Elements: Map<int, SVal>
      Length: int option
      /// Where the object was created: method and instruction offset.
      Site: (MethodRef * int) option
      /// The user code that led to its creation: the site itself in user code, else the nearest user call.
      UserSite: (MethodRef * int) option
      /// Conditions the call that returned this object checked on the way: they hold when it is tested.
      Guard: Predicate list }

type PathState =
    { Heap: Map<int, HeapObj>
      Statics: Map<string, SVal>
      Predicates: Predicate list
      /// The most recent function object created in user code that this path invoked.
      LastUserObject: (MethodRef * int) option
      /// The path passed a condition on the request that could not be read (see Condition.Uncertain).
      Uncertain: bool }

type Phase =
    | Registration
    | RequestPhase

type HandlerRef =
    /// A user function.
    | HandlerMethod of MethodRef
    /// No user function was reached; the user code that created the last function object invoked.
    | HandlerSite of MethodRef * int

type Hit =
    { Handler: HandlerRef
      Predicates: Predicate list }

/// Something registered with the platform during the registration phase.
type Registered =
    | Endpoint of
        rule: Rule *
        template: SVal *
        prefix: string list option *
        httpMethods: SVal option *
        handler: SVal *
        builder: int option *
        state: PathState *
        site: (MethodRef * int)
    | RequestRoot of rule: Rule * handler: SVal * state: PathState * site: (MethodRef * int)

type Limits =
    { Steps: int
      Depth: int
      ThreadsPerCall: int
      VisitsPerInstruction: int
      OutcomesPerCall: int }

let defaultLimits =
    { Steps = 2_000_000
      Depth = 36
      ThreadsPerCall = 64
      VisitsPerInstruction = 10
      OutcomesPerCall = 4 }

let private intTypes =
    set [ "System.Int32"; "System.Int64"; "System.Int16"; "System.Byte"; "System.SByte"; "System.UInt32"; "System.UInt64"; "System.UInt16"; "System.Boolean"; "System.Char"; "System.Double"; "System.Single"; "System.IntPtr" ]

let private defaultOf (typeName: string) =
    if intTypes.Contains typeName then Const(box 0L) else Null

let private elementTypeName (fieldType: string) =
    match fieldType.IndexOf '<' with
    | -1 -> fieldType
    | i -> fieldType.Substring(0, i)

/// Two predicates that cannot both hold.
let private conflicts (a: Predicate) (b: Predicate) =
    a.Operator = PredicateOperator.Equals
    && b.Operator = PredicateOperator.Equals
    && a.Part = b.Part
    && a.Index = b.Index
    && not (String.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase))

let private addPredicates (state: PathState) (ps: Predicate list) : PathState option =
    if ps.IsEmpty then
        Some state
    elif ps |> List.exists (fun p -> state.Predicates |> List.exists (conflicts p)) then
        None
    else
        Some { state with Predicates = state.Predicates @ (ps |> List.filter (fun p -> not (List.contains p state.Predicates))) }

let private describe (tags: Tags) = RouteRecovery.describe tags

/// A member's name without an explicit interface prefix ("System.Collections.IEnumerator.MoveNext" -> "MoveNext").
let private shortName (name: string) = name.Substring(name.LastIndexOf '.' + 1)

/// Callbacks the platform calls while building the request pipeline take one of these: an application or
/// endpoint builder, or a request context. Other callbacks (service factories, options) do not route.
let private isPipelineType (typeKey: string) =
    [ "Microsoft.AspNetCore.Builder."; "Microsoft.AspNetCore.Routing."; "Microsoft.AspNetCore.Http." ]
    |> List.exists typeKey.StartsWith

type private Thread =
    { Pc: int
      Stack: SVal list
      Locals: Map<int, SVal>
      Args: Map<int, SVal>
      Visits: Map<int, int>
      State: PathState
      Constrained: TypeRef option }

/// The interpreter. One machine per application and phase run.
type Machine(set: AssemblySet, matcher: Matcher, recognizers: ICodegenRecognizer list, limits: Limits) =
    let mutable nextId = 0
    let mutable steps = 0
    // Steps may not pass the deadline; static initialization runs under a tighter one.
    let mutable deadline = limits.Steps
    // Static fields being initialized: their initializer stops once the field is stored.
    let initializing = HashSet<string>()
    // Debugging only: the methods being interpreted, innermost last.
    let callStack = Stack<string>()
    // The innermost call made from user code, for attributing objects that libraries create on its behalf.
    let mutable currentUserSite: (MethodRef * int) option = None
    let hits = ResizeArray<Hit>()
    let registered = ResizeArray<Registered>()
    // User function objects handed to library code while the application registers its routes.
    let handedOver = Dictionary<string, MethodRef * (MethodRef * int)>()
    let forwarders = Dictionary<string, bool>()
    let initializers = Dictionary<string, MethodRef list>()

    let isUser (m: MethodRef) = set.IsUser m.Type.Assembly
    let debugLevel = match System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG" with "1" -> 1 | "2" -> 2 | _ -> 0
    let debug = debugLevel > 0
    let traceMethod = match System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG_METHOD" with null -> "" | m -> m
    let trace (text: string) = if debug then eprintfn "  [symbolic] %s" text

    let interpretable (m: MethodRef) =
        match set.Origin m.Type.Assembly with
        | Framework -> false
        | _ -> true

    let methodOf (m: MethodRef) = set.TryMethod m

    let typeName (s: PathState) (v: SVal) =
        match v with
        | Ref id ->
            match s.Heap.TryFind id with
            | Some o -> Some o.Type
            | None -> None
        | _ -> None

    let allocate (s: PathState) (t: TypeRef) (site: (MethodRef * int) option) =
        nextId <- nextId + 1
        let id = nextId

        let userSite =
            match site with
            | Some(m, _) when set.IsUser m.Type.Assembly -> site
            | _ -> currentUserSite

        id,
        { s with
            Heap =
                s.Heap.Add(
                    id,
                    { Type = t
                      Fields = Map.empty
                      Elements = Map.empty
                      Length = None
                      Site = site
                      UserSite = userSite
                      Guard = [] }
                ) }

    /// The value an address points to; other values are themselves.
    let deref (s: PathState) (v: SVal) =
        match v with
        | FieldAddress(id, field) ->
            match s.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind field) with
            | Some x -> x
            | None -> Null
        | other -> other

    let update (s: PathState) (id: int) (f: HeapObj -> HeapObj) =
        match s.Heap.TryFind id with
        | Some o -> { s with Heap = s.Heap.Add(id, f o) }
        | None -> s

    let isEqualityMethod (m: MethodRef) =
        recognizers |> List.exists (fun r -> r.IsStringEquality m)
        || (m.Type.FullName = "System.String" && (m.Name = "Equals" || m.Name = "op_Equality"))
        || (m.Type.FullName = "Microsoft.AspNetCore.Http.HttpMethods" && m.Name = "Equals")
        || (m.Type.FullName = "Microsoft.AspNetCore.Http.PathString" && (m.Name = "Equals" || m.Name = "op_Equality"))

    let predicate (part: RequestPart, index: int option) (op: PredicateOperator) (value: string) =
        { Part = part
          Operator = op
          Index = index
          Value = value
          Location = None }

    /// A comparison of request data with a constant, as a condition.
    let compare (a: SVal) (b: SVal) (op: PredicateOperator) : SVal option =
        match a, b with
        | Req tags, Const(:? string as s)
        | Const(:? string as s), Req tags ->
            describe tags |> Option.map (fun subject -> Bool { WhenTrue = [ predicate subject op s ]; WhenFalse = []; Uncertain = false })
        | Const x, Const y when op = PredicateOperator.Equals -> Some(Const(box (if x.Equals y then 1L else 0L)))
        | (Unknown | Null), Const(:? string)
        | Const(:? string), (Unknown | Null) -> Some(Bool { WhenTrue = []; WhenFalse = []; Uncertain = true })
        | _ -> None

    /// Pure functions of System.String evaluated on constants.
    let fold (m: MethodRef) (operands: SVal list) : SVal option =
        let consts = operands |> List.map (function Const c -> Some c | Null -> Some null | _ -> None)

        if m.Type.FullName <> "System.String" || consts |> List.exists Option.isNone then
            None
        else
            let values = consts |> List.map Option.get

            try
                let convert (typeName: string) (v: obj) : obj =
                    match typeName, v with
                    | "System.Int32", (:? int64 as n) -> box (int n)
                    | "System.Char", (:? int64 as n) -> box (char n)
                    | "System.Boolean", (:? int64 as n) -> box (n <> 0L)
                    | _ -> v

                let candidates =
                    typeof<string>.GetMethods()
                    |> Array.filter (fun r ->
                        r.Name = m.Name
                        && r.IsStatic = not m.HasThis
                        && r.GetParameters().Length = m.ParameterCount
                        && (r.GetParameters() |> Array.map (fun p -> p.ParameterType.FullName) |> Array.toList) = m.ParameterTypes)

                match candidates with
                | [| r |] ->
                    let parameters = r.GetParameters() |> Array.map (fun p -> p.ParameterType.FullName)

                    let result =
                        if m.HasThis then
                            let target = values.Head
                            let args = List.zip (List.ofArray parameters) values.Tail |> List.map (fun (t, v) -> convert t v)
                            r.Invoke(target, List.toArray args)
                        else
                            let args = List.zip (List.ofArray parameters) values |> List.map (fun (t, v) -> convert t v)
                            r.Invoke(null, List.toArray args)

                    match result with
                    | null -> Some Null
                    | :? string as s -> Some(Const s)
                    | :? bool as b -> Some(Const(box (if b then 1L else 0L)))
                    | :? int as n -> Some(Const(box (int64 n)))
                    | :? char as c -> Some(Const(box (int64 c)))
                    | _ -> Some Unknown
                | _ -> None
            with _ ->
                None

    /// Whether a user method only passes its arguments on to a library function (a partial application),
    /// in which case it is not the handler.
    let isForwarder (m: MethodRef) =
        match forwarders.TryGetValue m.Key with
        | true, f -> f
        | _ ->
            let result =
                match methodOf m with
                | Some(def, owner) when def.IsCompilerGenerated || owner.IsCompilerGenerated ->
                    match def.Body.Value with
                    | Some body ->
                        let calls =
                            body.Instructions
                            |> Array.choose (fun i ->
                                match i.Op with
                                | Call t
                                | CallVirt t
                                | NewObj t -> Some t
                                | _ -> None)

                        // Passing on to user code is still forwarding: the user code it reaches is the handler.
                        let effects = calls |> Array.exists (fun t -> (matcher.Sink t).IsSome)
                        not effects && calls.Length <= 3
                    | None -> false
                | _ -> false

            forwarders[m.Key] <- result
            result

    /// Methods that store a static field, used when a field has no static constructor to initialize it.
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
                        m.Ref.ParameterCount = 0
                        && (match m.Body.Value with
                            | Some b -> b.Instructions |> Array.exists (fun i -> i.Op = StSFld f)
                            | None -> false))
                    |> List.map (fun m -> m.Ref)
                | None -> []

            initializers[f.Key] <- ms
            ms

    member _.Hits = List.ofSeq hits

    /// User functions passed to library code during registration: the method each will run, and where it was
    /// handed over. Those never reached by a request path are still handlers, with no route recovered.
    member _.HandedOver = handedOver.Values |> List.ofSeq |> List.filter (fun (f, _) -> not (isForwarder f))
    member _.Registered = List.ofSeq registered
    member _.Exhausted = steps >= limits.Steps
    member _.Steps = steps

    /// Runs a method with arguments; returns the possible results with their states.
    member this.Invoke(phase: Phase, m: MethodRef, args: SVal list, state: PathState, depth: int) : (SVal * PathState) list =
        if steps >= deadline || depth > limits.Depth then
            [ Unknown, state ]
        else
            match methodOf m with
            | Some(def, _) when interpretable m && not def.IsAbstract && def.Body.Value.IsSome ->
                // A library call that runs too long yields an unknown value instead of ending the path.
                let saved = deadline
                let own = set.Origin m.Type.Assembly = Dependency && depth >= 2

                if own then
                    deadline <- min saved (steps + 150_000)

                let finish (results: (SVal * PathState) list) =
                    let exhausted = own && steps >= deadline && saved > deadline
                    deadline <- saved
                    if results.IsEmpty && exhausted then [ Unknown, state ] else results

                if debug && (phase = RequestPhase || depth < 4 || debugLevel > 1) then
                    let show (v: SVal) =
                        match v with
                        | Ref id -> (match state.Heap.TryFind id with Some o -> "ref " + o.Type.Name | None -> "ref?")
                        | Fn(f, _) -> "fn " + f.Display
                        | Const c -> string c
                        | Req _ -> "req"
                        | Bool _ -> "bool"
                        | Null -> "null"
                        | FieldAddress _ -> "addr"
                        | Unknown -> "?"

                    trace (sprintf "%A d%d enter %s (%s)" phase depth m.Display (args |> List.map show |> String.concat ", "))

                if debug then callStack.Push m.Display

                let results = this.Interpret(phase, def, def.Body.Value.Value, args, state, depth)

                if debug then callStack.Pop() |> ignore

                // Conditions checked inside the call are scoped to it: on return they travel with the result
                // (a boolean's true side, or a guard on a returned object), not with the path.
                let entry = state.Predicates.Length

                let scoped =
                    results
                    |> List.map (fun (result, s) ->
                        let inner = s.Predicates |> List.skip (min entry s.Predicates.Length)

                        if inner.IsEmpty then
                            result, s
                        else
                            let s = { s with Predicates = s.Predicates |> List.truncate entry }

                            match result with
                            | Bool c -> Bool { c with WhenTrue = c.WhenTrue @ inner }, s
                            | Ref id -> result, update s id (fun o -> { o with Guard = o.Guard @ inner })
                            | other -> other, s)

                finish scoped
            | _ -> [ Unknown, state ]

    /// The elements of a collection value, when they are known: a modelled framework collection, an array, or
    /// an F# list (followed through its head and tail fields).
    member _.ElementsOf(state: PathState, v: SVal) : SVal list option =
        match v with
        | Ref id ->
            match state.Heap.TryFind id with
            | Some o when not o.Elements.IsEmpty || o.Length = Some 0 -> Some(o.Elements |> Map.toList |> List.sortBy fst |> List.map snd)
            | Some o when o.Type.FullName = "Microsoft.FSharp.Collections.FSharpList`1" ->
                let rec walk (v: SVal) (acc: SVal list) (guard: int) =
                    match v with
                    | Ref cell when guard < 256 ->
                        match state.Heap.TryFind cell with
                        | Some c ->
                            match c.Fields.TryFind "tail", c.Fields.TryFind "head" with
                            | Some(Ref _ as tail), Some head -> walk tail (head :: acc) (guard + 1)
                            | _ -> Some(List.rev acc)
                        | None -> None
                    | _ -> Some(List.rev acc)

                walk v [] 0
            | Some o when o.Type.FullName.StartsWith "System.Collections.Generic." -> Some []
            | _ -> None
        | _ -> None

    /// The user method a function value runs, following compiler-generated wrappers.
    member this.UserFunction(v: SVal, state: PathState) : MethodRef option =
        let rec unwrap (m: MethodRef) (guard: int) =
            if guard > 4 then
                Some m
            else
                match methodOf m with
                | Some(def, owner) when (def.IsCompilerGenerated || owner.IsCompilerGenerated) ->
                    match def.Body.Value with
                    | Some body ->
                        let inner =
                            body.Instructions
                            |> Array.tryPick (fun i ->
                                match i.Op with
                                | Call t
                                | CallVirt t when isUser t && not (MethodRef.isConstructor t) && not (t.Name.StartsWith "get_") -> Some t
                                | _ -> None)

                        match inner with
                        | Some t -> unwrap t (guard + 1)
                        | None -> Some m
                    | None -> Some m
                | _ -> Some m

        match v with
        | Fn(m, _) when isUser m -> unwrap m 0
        | Ref id ->
            match state.Heap.TryFind id with
            | Some o when isUser { Type = o.Type; Name = ""; Key = ""; LocalKey = ""; ParameterTypes = []; ReturnType = ""; HasThis = false; GenericArity = 0; TypeArguments = [] } ->
                match set.TryType o.Type with
                | Some(def, _) when set.BaseChain o.Type |> List.exists (fun b -> b.FullName.StartsWith "Microsoft.FSharp.Core.FSharpFunc" || b.FullName.StartsWith "Microsoft.FSharp.Core.OptimizedClosures") ->
                    def.Methods
                    |> List.tryFind (fun m -> m.Ref.Name = "Invoke" && m.Body.Value.IsSome)
                    |> Option.bind (fun m -> unwrap m.Ref 0)
                | _ -> None
            | _ -> None
        | _ -> None

    /// Calls a function value (delegate, closure or method pointer) with arguments.
    member this.CallValue(phase: Phase, f: SVal, args: SVal list, state: PathState, depth: int) : (SVal * PathState) list =
        match f with
        | Fn(m, target) ->
            let all = if m.HasThis then (defaultArg target Unknown) :: args else args
            this.Dispatch(phase, m, all, state, depth, None)
        | Ref id ->
            match state.Heap.TryFind id with
            | Some o ->
                match set.TryType o.Type with
                | Some(def, _) ->
                    let invoke =
                        set.BaseChain o.Type
                        |> List.tryPick (fun t ->
                            match set.TryType t with
                            | Some(d, _) ->
                                d.Methods
                                |> List.tryFind (fun x -> x.Ref.Name = "Invoke" && not x.IsAbstract && x.Ref.ParameterCount = args.Length && x.Body.Value.IsSome)
                            | None -> None)

                    match invoke with
                    | Some inv -> this.Dispatch(phase, inv.Ref, f :: args, state, depth, None)
                    | None -> [ Unknown, state ]
                | None -> [ Unknown, state ]
            | None -> [ Unknown, state ]
        | _ -> [ Unknown, state ]

    /// A call to a method that will run: handler detection, then interpretation or the framework model.
    member this.Dispatch(phase: Phase, target: MethodRef, operands: SVal list, state: PathState, depth: int, site: (MethodRef * int) option) =
        // Remember user-created function objects invoked on the way (for the leaf fallback).
        let state =
            match target.HasThis, operands with
            | true, Ref id :: _ ->
                match state.Heap.TryFind id with
                | Some { UserSite = Some(siteMethod, offset) } when target.Name = "Invoke" || target.Name = "Specialize" ->
                    { state with LastUserObject = Some(siteMethod, offset) }
                | _ -> state
            | _ -> state

        let accessor = target.Name.StartsWith "get_" || target.Name.StartsWith "set_"

        if phase = RequestPhase && isUser target && not (MethodRef.isConstructor target) && not accessor && not (isForwarder target) then
            // The first user function reached on this path handles the request; the path ends here.
            if debug then
                let nl = System.Environment.NewLine
                eprintfn "  [symbolic] HIT %s %A%s      %s" target.Display (state.Predicates |> List.map (fun p -> p.Part, p.Value)) nl (callStack |> Seq.truncate 25 |> String.concat (nl + "      "))

            hits.Add
                { Handler = HandlerMethod target
                  Predicates = if state.Uncertain then [] else state.Predicates }

            []
        elif interpretable target && not (recognizers |> List.exists (fun r -> r.IsOpaque target)) then
            if phase = Registration && not (isUser target) then
                for v in operands do
                    match this.UserFunction(v, state) with
                    | Some f when not (handedOver.ContainsKey f.Key) -> handedOver[f.Key] <- (f, defaultArg site (target, 0))
                    | _ -> ()

            this.Invoke(phase, target, operands, state, depth + 1)
        else
            this.Framework(phase, target, operands, state, depth, site)

    /// The model of a shared-framework call.
    member this.Framework(phase: Phase, m: MethodRef, operands: SVal list, state: PathState, depth: int, site: (MethodRef * int) option) : (SVal * PathState) list =
        if debug then
            let show (v: SVal) =
                match v with
                | Const c -> string c
                | Ref id -> "ref " + (match state.Heap.TryFind id with Some o -> o.Type.FullName | None -> "?")
                | Fn(f, _) -> "fn " + f.Name
                | Req _ -> "req"
                | Bool _ -> "bool"
                | Null -> "null"
                | FieldAddress _ -> "addr"
                | Unknown -> "?"

            trace (sprintf "%A d%d framework %s (%s)" phase depth m.Display (operands |> List.map show |> String.concat ", "))

        let receiver = match operands with first :: _ when m.HasThis -> Some first | _ -> None
        let arg (i: int) = List.tryItem (if m.HasThis then i + 1 else i) operands |> Option.defaultValue Unknown
        let tags = operands |> List.fold (fun acc v -> match v with Req t -> Set.union acc t | _ -> acc) Set.empty
        let one v = [ v, state ]

        let siteOrUnknown = defaultArg site (m, 0)

        match matcher.Source m with
        | Some rule ->
            let tag =
                match rule.RequestPart, rule.Category with
                | Some p, _ -> Part p
                | None, Some EffectCategory.Network -> Connection
                | None, _ -> HttpContext

            one (Req(Set.singleton tag))
        | None ->

        match matcher.ConstantGetter m with
        | Some "false" when phase = RequestPhase -> one (Const(box 0L))
        | Some "true" when phase = RequestPhase -> one (Const(box 1L))
        | _ ->

        match matcher.Comparison m with
        | Some rule when rule.RequestPart.IsSome && rule.ComparesTo.IsSome ->
            match arg 0 with
            | Req _ -> one (Bool { WhenTrue = [ predicate (rule.RequestPart.Value, None) PredicateOperator.Equals rule.ComparesTo.Value ]; WhenFalse = []; Uncertain = false })
            | _ -> one Unknown
        | _ ->

        if isEqualityMethod m && operands.Length >= 2 then
            match compare operands[0] operands[1] PredicateOperator.Equals with
            | Some v -> one v
            | None -> one Unknown
        elif m.Type.FullName = "System.String" && (m.Name = "StartsWith" || m.Name = "EndsWith" || m.Name = "Contains") && operands.Length >= 2 && (match operands[0] with Req _ -> true | _ -> false) then
            let op =
                match m.Name with
                | "StartsWith" -> PredicateOperator.StartsWith
                | "EndsWith" -> PredicateOperator.EndsWith
                | _ -> PredicateOperator.Contains

            match compare operands[0] operands[1] op with
            | Some v -> one v
            | None -> one Unknown
        elif m.Type.FullName = "Microsoft.AspNetCore.Http.PathString" && m.Name = "StartsWithSegments" && operands.Length >= 2 then
            match compare operands[0] operands[1] PredicateOperator.StartsWith with
            | Some v -> one v
            | None -> one Unknown
        elif m.Type.FullName = "System.Text.RegularExpressions.Regex" && (m.Name = "IsMatch" || m.Name = "Match") then
            let input, pattern =
                if m.HasThis then
                    let p =
                        match receiver with
                        | Some(Ref id) -> state.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "arg0")
                        | _ -> None

                    arg 0, defaultArg p Unknown
                else
                    arg 0, arg 1

            match input, pattern with
            | Req t, Const(:? string as p) ->
                match describe t with
                | Some subject ->
                    let cond = Bool { WhenTrue = [ predicate subject PredicateOperator.Matches p ]; WhenFalse = []; Uncertain = false }

                    if m.Name = "IsMatch" then
                        one cond
                    else
                        // A Match object whose Success is the condition.
                        let id, s = allocate state { Assembly = "System.Text.RegularExpressions"; FullName = "System.Text.RegularExpressions.Match" } None
                        [ Ref id, update s id (fun o -> { o with Fields = o.Fields.Add("Success", cond) }) ]
                | None -> one Unknown
            | _ -> one Unknown
        elif m.Type.FullName.StartsWith "System.Text.RegularExpressions.Group" || m.Type.FullName = "System.Text.RegularExpressions.Match" then
            match receiver, m.Name with
            | Some(Ref id), "get_Success" ->
                one (state.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "Success") |> Option.defaultValue Unknown)
            | _ -> one Unknown
        elif m.Name = "Start" && m.Type.FullName.Contains "MethodBuilder" then
            // An async builder starts its state machine right away.
            match arg 0 with
            | Ref id ->
                match state.Heap.TryFind id with
                | Some o ->
                    match set.Implementation(o.Type, { m with Name = "MoveNext"; ParameterTypes = []; Key = ""; LocalKey = "" }) with
                    | Some moveNext ->
                        this.Dispatch(phase, moveNext, [ Ref id ], state, depth, site) |> List.map (fun (_, s) -> Unknown, s)
                    | None ->
                        match set.TryType o.Type with
                        | Some(def, _) ->
                            match def.Methods |> List.tryFind (fun x -> x.Ref.Name.EndsWith "MoveNext" && x.Body.Value.IsSome) with
                            | Some mn -> this.Dispatch(phase, mn.Ref, [ Ref id ], state, depth, site) |> List.map (fun (_, s) -> Unknown, s)
                            | None -> one Unknown
                        | None -> one Unknown
                | None -> one Unknown
            | _ -> one Unknown
        elif m.Name = "ToString" && (match receiver with Some(Ref id) -> (state.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "value__")).IsSome | _ -> false) then
            // An enum value's name.
            match receiver with
            | Some(Ref id) ->
                let o = state.Heap[id]

                match set.TryType o.Type, o.Fields["value__"] with
                | Some(d, _), Const value ->
                    match d.Literals |> List.tryFind (fun (_, v) -> v.Equals value) with
                    | Some(name, _) -> one (Const name)
                    | None -> one Unknown
                | _ -> one Unknown
            | _ -> one Unknown
        elif m.Type.FullName.StartsWith "System.Threading.Tasks.Task" && m.Name = "FromResult" then
            // Completed tasks carry their result, so awaiting them gives it back.
            let id, s = allocate state { Assembly = "System.Runtime"; FullName = "<task>" } None
            [ Ref id, update s id (fun o -> { o with Fields = o.Fields.Add("<result>", arg 0) }) ]
        elif m.Type.FullName.Contains "MethodBuilder" && m.Name = "SetResult" then
            match receiver with
            | Some(Ref id) -> [ Unknown, update state id (fun o -> { o with Fields = o.Fields.Add("<result>", arg 0) }) ]
            | _ -> one Unknown
        elif m.Type.FullName.Contains "MethodBuilder" && m.Name = "get_Task" then
            match receiver with
            | Some(Ref id) ->
                let result = state.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "<result>") |> Option.defaultValue Unknown
                let t, s = allocate state { Assembly = "System.Runtime"; FullName = "<task>" } None
                [ Ref t, update s t (fun o -> { o with Fields = o.Fields.Add("<result>", result) }) ]
            | _ -> one Unknown
        elif (m.Name = "GetAwaiter" || m.Name = "ConfigureAwait" || m.Name = "AsTask")
             && (match receiver with Some(Ref id) -> (typeName state (Ref id) |> Option.map (fun t -> t.FullName)) = Some "<task>" | _ -> false) then
            one receiver.Value
        elif (m.Name = "GetResult" || m.Name = "get_Result")
             && (match receiver with Some(Ref id) -> (typeName state (Ref id) |> Option.map (fun t -> t.FullName)) = Some "<task>" | _ -> false) then
            match receiver with
            | Some(Ref id) -> one (state.Heap[id].Fields.TryFind "<result>" |> Option.defaultValue Unknown)
            | _ -> one Unknown
        elif m.Name = "get_IsCompleted" then
            // Awaits complete synchronously: the analysis follows the code, not the scheduling.
            one (Const(box 1L))
        elif m.Type.FullName = "Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory" && m.Name = "Parse" then
            one (arg 0)
        elif (Values.isTransparent m) && not operands.IsEmpty then
            one operands.Head
        else

        match fold m operands with
        | Some v -> one v
        | None ->

        match matcher.Registration m with
        | Some rule when phase = Registration ->
            let roleArg (role: string) =
                rule.Payload |> List.tryFind (fun p -> p.Role = role) |> Option.map (fun p -> arg p.Argument)

            let builderPrefix () =
                match operands with
                | Ref id :: _ ->
                    match state.Heap.TryFind id with
                    | Some o ->
                        match o.Fields.TryFind "<prefix>" with
                        | Some(Const(:? string as p)) -> Some [ p ]
                        | Some _ -> None
                        | None -> Some []
                    | None -> Some []
                | _ -> Some []

            match roleArg "handler", roleArg "route-template", roleArg "route-prefix", roleArg "middleware", roleArg "middleware-arguments" with
            | Some handler, Some template, _, _, _ ->
                registered.Add(Endpoint(rule, template, builderPrefix (), roleArg "http-methods", handler, None, state, siteOrUnknown))
                one Unknown
            | Some handler, None, _, _, _ ->
                registered.Add(RequestRoot(rule, handler, state, siteOrUnknown))
                one Unknown
            | None, None, Some prefix, _, branch ->
                // A route group: a builder that prefixes everything mapped on it.
                let parent = builderPrefix ()

                let full =
                    match parent, prefix with
                    | Some ps, Const(:? string as p) -> Some(Const(PlatformRouting.normalizeRoute (ps @ [ p ])))
                    | _ -> None

                let id, s = allocate state { Assembly = "Microsoft.AspNetCore.Routing"; FullName = "Microsoft.AspNetCore.Routing.RouteGroupBuilder" } None
                let s = update s id (fun o -> { o with Fields = o.Fields.Add("<prefix>", defaultArg full Unknown) })

                match roleArg "branch" with
                | Some b ->
                    this.CallValue(phase, b, [ Ref id ], s, depth + 1) |> List.map (fun (_, s2) -> Unknown, s2)
                | None -> [ Ref id, s ]
            | None, None, None, Some middleware, _ ->
                // Use(next => context => ...): calling it with the next delegate gives the request handler.
                let produced = this.CallValue(phase, middleware, [ Unknown ], state, depth + 1)

                for (v, s) in produced do
                    match v with
                    | Fn _
                    | Ref _ -> registered.Add(RequestRoot(rule, v, s, siteOrUnknown))
                    | _ -> registered.Add(RequestRoot(rule, middleware, state, siteOrUnknown))

                one Unknown
            | None, None, None, None, Some arguments when not m.TypeArguments.IsEmpty ->
                // UseMiddleware<T>(args): the platform constructs T with the next delegate and the arguments,
                // then calls its Invoke or InvokeAsync per request.
                let t = m.TypeArguments.Head

                let extra =
                    match arguments with
                    | Ref id ->
                        match state.Heap.TryFind id with
                        | Some o -> o.Elements |> Map.toList |> List.sortBy fst |> List.map snd
                        | None -> []
                    | _ -> []

                match set.TryType t with
                | Some(def, _) ->
                    let ctor =
                        def.Methods
                        |> List.filter (fun x -> x.Ref.Name = ".ctor" && x.Body.Value.IsSome)
                        |> List.sortByDescending (fun x -> x.Ref.ParameterCount)
                        |> List.tryHead

                    let invoke =
                        def.Methods |> List.tryFind (fun x -> (x.Ref.Name = "Invoke" || x.Ref.Name = "InvokeAsync") && x.Body.Value.IsSome)

                    match ctor, invoke with
                    | Some c, Some inv ->
                        let id, s = allocate state t site
                        let ctorArgs = Ref id :: Unknown :: extra
                        let ctorArgs = (ctorArgs @ List.replicate 8 Unknown) |> List.truncate (c.Ref.ParameterCount + 1)

                        for (_, s2) in this.Invoke(Registration, c.Ref, ctorArgs, s, depth + 1) do
                            registered.Add(RequestRoot(rule, Fn(inv.Ref, Some(Ref id)), s2, siteOrUnknown))

                        one Unknown
                    | _ -> one Unknown
                | None -> one Unknown
            | _ -> one Unknown
        | _ ->

        // An endpoint builder that is built: its registration sees everything attached to it since.
        match receiver, m.Name with
        | Some(Ref id), "Build" ->
            for i in 0 .. registered.Count - 1 do
                match registered[i] with
                | Endpoint(rule, template, prefix, methods, handler, Some b, _, site) when b = id ->
                    registered[i] <- Endpoint(rule, template, prefix, methods, handler, Some b, state, site)
                | _ -> ()
        | _ -> ()

        // Framework collections hold the objects analyzed code puts in them, and can be enumerated. Explicit
        // interface implementations are named "Namespace.IFace.Member": match on the member alone.
        let shortName = m.Name.Substring(m.Name.LastIndexOf '.' + 1)

        match receiver, shortName with
        | Some(Ref id), ("Add" | "Enqueue" | "Push") when m.ParameterCount = 1 ->
            [ Unknown, update state id (fun o -> { o with Elements = o.Elements.Add(o.Elements.Count, arg 0) }) ]
        | Some(Ref id), "AddRange" ->
            match this.ElementsOf(state, arg 0) with
            | Some items ->
                [ Unknown,
                  update state id (fun o ->
                      { o with Elements = items |> List.fold (fun (es: Map<int, SVal>) v -> es.Add(es.Count, v)) o.Elements }) ]
            | None -> one Unknown
        | Some(Ref id), "GetEnumerator" ->
            match this.ElementsOf(state, Ref id) with
            | Some items ->
                let e, s = allocate state { Assembly = "System.Runtime"; FullName = "<enumerator>" } None
                [ Ref e, update s e (fun o -> { o with Elements = items |> List.indexed |> Map.ofList; Fields = o.Fields.Add("<position>", Const(box 0L)) }) ]
            | None -> one Unknown
        | Some(Ref id), "MoveNext" when (typeName state (Ref id) |> Option.map (fun t -> t.FullName)) = Some "<enumerator>" ->
            let o = state.Heap[id]
            let position = match o.Fields.TryFind "<position>" with Some(Const(:? int64 as n)) -> int n | _ -> 0

            if position < o.Elements.Count then
                [ Const(box 1L), update state id (fun o -> { o with Fields = o.Fields.Add("<position>", Const(box (int64 (position + 1)))) }) ]
            else
                one (Const(box 0L))
        | Some(Ref id), "get_Current" when (typeName state (Ref id) |> Option.map (fun t -> t.FullName)) = Some "<enumerator>" ->
            let o = state.Heap[id]
            let position = match o.Fields.TryFind "<position>" with Some(Const(:? int64 as n)) -> int n | _ -> 0
            one (o.Elements.TryFind(position - 1) |> Option.defaultValue Unknown)
        | Some(Ref id), name when
            (name.StartsWith "get_Item" && name.Length > 8 && Char.IsDigit name[8]
             || name = "get_Key"
             || name = "get_Value")
            && (match state.Heap.TryFind id with
                | Some o -> o.Type.FullName.StartsWith "System.Tuple" || o.Type.FullName.StartsWith "System.Collections.Generic.KeyValuePair"
                | None -> false) ->
            // Tuples and key-value pairs return their constructor arguments.
            let index =
                match name with
                | "get_Key" -> 0
                | "get_Value" -> 1
                | _ -> int (name.Substring 8) - 1

            one (state.Heap[id].Fields.TryFind $"arg{index}" |> Option.defaultValue Unknown)
        | Some(Ref id), "get_Count" ->
            match this.ElementsOf(state, Ref id) with
            | Some items -> one (Const(box (int64 items.Length)))
            | None -> one Unknown
        | Some(Ref id), "get_Item" when m.ParameterCount = 1 ->
            match state.Heap.TryFind id, arg 0 with
            | Some o, Const(:? int64 as i) -> one (o.Elements.TryFind(int i) |> Option.defaultValue Unknown)
            | _ -> one Unknown
        | Some(Ref id), "get_Metadata" ->
            match state.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "<metadata>") with
            | Some v -> one v
            | None ->
                let listId, s = allocate state { Assembly = "System.Runtime"; FullName = "System.Collections.Generic.List`1" } None
                [ Ref listId, update s id (fun o -> { o with Fields = o.Fields.Add("<metadata>", Ref listId) }) ]
        | _ ->

        if m.Name = "Empty" && m.Type.FullName = "System.Array" then
            let id, s = allocate state { Assembly = "System.Runtime"; FullName = "System.Array" } None
            [ Ref id, update s id (fun o -> { o with Length = Some 0 }) ]
        else

        // Registration phase: objects the platform calls back (an endpoint data source) are called when they are
        // handed to it, with everything set up so far.
        let state =
            if phase = Registration && not (MethodRef.isConstructor m) then
                // The receiver of a call is not handed over; its arguments are.
                (if m.HasThis then List.tail operands else operands)
                |> List.fold
                    (fun (s: PathState) v ->
                        match v with
                        | Ref id ->
                            match s.Heap.TryFind id with
                            | Some o ->
                                matcher.Callbacks o.Type
                                |> List.collect (fun rule -> rule.Members)
                                |> List.choose (fun memberName ->
                                    set.Implementation(o.Type, { m with Name = memberName; ParameterTypes = []; Key = ""; LocalKey = ""; HasThis = true }))
                                |> List.fold
                                    (fun (s2: PathState) impl ->
                                        match this.Invoke(Registration, impl, [ Ref id ], s2, depth + 1) with
                                        | (_, s3) :: _ -> s3
                                        | [] -> s2)
                                    s
                            | None -> s
                        | _ -> s)
                    state
            else
                state

        // Registration phase: function values handed to the platform will be called by it.
        let state =
            if phase = Registration then
                operands
                |> List.indexed
                |> List.filter (fun (i, _) -> not (m.HasThis && i = 0))
                |> List.fold
                    (fun (s: PathState) (_, v) ->
                        match v with
                        | Fn(f, _) when f.ParameterTypes |> List.exists isPipelineType ->
                            match this.CallValue(phase, v, List.replicate f.ParameterCount Unknown, s, depth + 1) with
                            | (_, s2) :: _ -> s2
                            | [] -> s
                        | _ -> s)
                    state
            else
                state

        if tags.IsEmpty then
            [ Unknown, state ]
        else
            // Tags survive the call, moved by list and array access.
            let t = Taint.transferValues recognizers m (operands |> List.map (function Req t -> t | _ -> Set.empty)) (operands |> List.map (function Const(:? int64 as n) -> Some(int n) | _ -> None))
            [ (if t.IsEmpty then Unknown else Req t), state ]

    /// Interprets one method body.
    member this.Interpret(phase: Phase, def: MethodDef, body: MethodBody, args: SVal list, state: PathState, depth: int) : (SVal * PathState) list =
        let instrs = body.Instructions
        let indexOf = Dictionary<int, int>()
        instrs |> Array.iteri (fun i ins -> indexOf[ins.Offset] <- i)
        let me = def.Ref

        let localDefault (n: int) =
            match List.tryItem n body.LocalTypes with
            | Some t -> defaultOf t.FullName
            | None -> Unknown

        let outcomes = ResizeArray<SVal * PathState>()
        let threads = Stack<Thread>()
        let mutable created = 1

        threads.Push
            { Pc = 0
              Stack = []
              Locals = Map.empty
              Args = args |> List.indexed |> Map.ofList
              Visits = Map.empty
              State = state
              Constrained = None }

        let fork (t: Thread) =
            if created < (if phase = RequestPhase then limits.ThreadsPerCall * 4 else limits.ThreadsPerCall) then
                created <- created + 1
                threads.Push t

        let jump (t: Thread) (offset: int) =
            match indexOf.TryGetValue offset with
            | true, i -> Some { t with Pc = i }
            | _ -> None

        let truth (v: SVal) : bool option =
            match v with
            | Const(:? int64 as n) -> Some(n <> 0L)
            | Const(:? string) -> Some true
            | Const _ -> Some true
            | Null -> Some false
            | Ref _
            | Fn _ -> Some true
            | _ -> None

        /// The two ways of a branch on v: (taken when v is true, predicates for true, for false).
        let conditionOf (v: SVal) : Condition =
            match v with
            | Bool c -> c
            | Req t ->
                // A list's tail is null exactly when the list has ended.
                match t |> Seq.tryPick (function SegmentsFrom n -> Some n | _ -> None) with
                | Some n -> { WhenTrue = []; WhenFalse = [ predicate (RequestPart.SegmentCount, None) PredicateOperator.Equals (string (n - 1)) ]; Uncertain = false }
                | None -> { WhenTrue = []; WhenFalse = []; Uncertain = false }
            | _ -> { WhenTrue = []; WhenFalse = []; Uncertain = false }

        let branch (t: Thread) (v: SVal) (takenWhenTrue: bool) (target: int) =
            let next = { t with Pc = t.Pc + 1 }

            // A returned object that carries the conditions under which it was produced: testing it applies them.
            let t, next =
                match v with
                | Ref id ->
                    match t.State.Heap.TryFind id with
                    | Some o when not o.Guard.IsEmpty ->
                        match addPredicates t.State o.Guard with
                        | Some s -> { t with State = s }, { next with State = s }
                        | None -> { t with Pc = Int32.MaxValue }, { next with Pc = Int32.MaxValue }
                    | _ -> t, next
                | _ -> t, next

            match truth v with
            | Some b ->
                if b = takenWhenTrue then
                    jump t target |> Option.iter fork
                else
                    fork next
            | None ->
                let c = conditionOf v
                let whenTaken, whenNot = if takenWhenTrue then c.WhenTrue, c.WhenFalse else c.WhenFalse, c.WhenTrue

                // Taking the true side of an unreadable comparison makes the path uncertain.
                let mark (s: PathState) (isTrueSide: bool) = if c.Uncertain && isTrueSide then { s with Uncertain = true } else s

                let taken () =
                    match addPredicates (mark t.State takenWhenTrue) whenTaken with
                    | Some s -> jump { t with State = s } target |> Option.iter fork
                    | None -> ()

                let notTaken () =
                    match addPredicates (mark t.State (not takenWhenTrue)) whenNot with
                    | Some s -> fork { next with State = s }
                    | None -> ()

                // Threads run last-in first-out: the side that narrows the request is pushed last, so it runs
                // first and reaches its handler before the budget is spent on the others.
                if whenTaken.Length >= whenNot.Length then
                    notTaken ()
                    taken ()
                else
                    taken ()
                    notTaken ()

        let equal (a: SVal) (b: SVal) : SVal =
            match a, b with
            | Const x, Const y -> Const(box (if x.Equals y then 1L else 0L))
            | Null, Null -> Const(box 1L)
            | (Ref _ | Fn _ | Const _), Null
            | Null, (Ref _ | Fn _ | Const _) -> Const(box 0L)
            | Ref x, Ref y -> Const(box (if x = y then 1L else 0L))
            | Req _, Const _
            | Const _, Req _ ->
                match compare a b PredicateOperator.Equals with
                | Some v -> v
                | None -> Unknown
            | Bool c, Const(:? int64 as n) when n = 0L -> Bool { c with WhenTrue = c.WhenFalse; WhenFalse = c.WhenTrue }
            | Bool c, Const(:? int64 as n) when n = 1L -> Bool c
            | _ -> Unknown

        while threads.Count > 0 && steps < deadline do
            let mutable t = threads.Pop()
            let mutable running = true

            while running && steps < deadline do
                steps <- steps + 1

                if t.Pc >= instrs.Length then
                    running <- false
                else
                    let ins = instrs[t.Pc]
                    let visits = t.Visits.TryFind t.Pc |> Option.defaultValue 0

                    if traceMethod <> "" && me.Display.Contains traceMethod then
                        let show (v: SVal) =
                            match v with
                            | Const c -> sprintf "%A" c
                            | Ref id -> "ref#" + string id + " " + (match t.State.Heap.TryFind id with Some o -> o.Type.Name + " " + (o.Fields |> Map.toList |> List.map fst |> String.concat ",") | None -> "?")
                            | other -> sprintf "%A" other

                        eprintfn "    [%s] %04x %A  stack: %s" me.Name ins.Offset ins.Op (t.Stack |> List.truncate 3 |> List.map show |> String.concat " | ")

                    if visits > limits.VisitsPerInstruction then
                        running <- false
                    else
                        t <- { t with Visits = t.Visits.Add(t.Pc, visits + 1) }
                        let next = { t with Pc = t.Pc + 1; Constrained = None }

                        let pop n =
                            let popped = t.Stack |> List.truncate n
                            List.rev popped, t.Stack |> List.skip (min n t.Stack.Length)

                        let push (v: SVal) (rest: SVal list) = { next with Stack = v :: rest }

                        let continueWith (results: (SVal * PathState) list) (rest: SVal list) (returnsValue: bool) =
                            match results |> List.truncate (if phase = RequestPhase then limits.OutcomesPerCall * 4 else limits.OutcomesPerCall) with
                            | [] -> running <- false
                            | (v, s) :: others ->
                                for (v2, s2) in others do
                                    fork { next with Stack = (if returnsValue then v2 :: rest else rest); State = s2 }

                                t <- { next with Stack = (if returnsValue then v :: rest else rest); State = s }

                        match ins.Op with
                        | Nop -> t <- next
                        | LdArg n -> t <- push (t.Args.TryFind n |> Option.defaultValue Unknown) t.Stack
                        | LdArgA n -> t <- push (t.Args.TryFind n |> Option.defaultValue Unknown) t.Stack
                        | StArg n ->
                            let v, rest = pop 1
                            t <- { next with Stack = rest; Args = t.Args.Add(n, v.Head) }
                        | LdLoc n -> t <- push (t.Locals.TryFind n |> Option.defaultValue (localDefault n)) t.Stack
                        | LdLocA n ->
                            match t.Locals.TryFind n with
                            | Some(Ref id) -> t <- push (Ref id) t.Stack
                            // The address of a local that holds request data or a constant reads as that value.
                            | Some((Req _ | Const _ | Fn _ | Bool _ | Null | Unknown) as v) -> t <- push v t.Stack
                            | _ ->
                                // A struct local that was never assigned: give it an object so writes through the
                                // address land somewhere.
                                let valueType =
                                    match List.tryItem n body.LocalTypes with
                                    | Some lt -> (match set.TryType lt with Some(d, _) -> d.IsValueType | None -> false)
                                    | None -> false

                                match List.tryItem n body.LocalTypes with
                                | Some lt when valueType ->
                                    let id, s = allocate t.State lt (Some(me, ins.Offset))
                                    t <- { push (Ref id) t.Stack with Locals = t.Locals.Add(n, Ref id); State = s }
                                | _ -> t <- push (localDefault n) t.Stack
                        | StLoc n ->
                            let v, rest = pop 1
                            t <- { next with Stack = rest; Locals = t.Locals.Add(n, v.Head) }
                        | LdStr s -> t <- push (Const s) t.Stack
                        | LdcI4 n -> t <- push (Const(box (int64 n))) t.Stack
                        | LdcI8 n -> t <- push (Const(box n)) t.Stack
                        | LdcR r -> t <- push (Const(box r)) t.Stack
                        | LdNull -> t <- push Null t.Stack
                        | LdFld f ->
                            let v, rest = pop 1

                            let value =
                                match deref t.State v.Head with
                                | Ref id ->
                                    match t.State.Heap.TryFind id with
                                    | Some o -> o.Fields.TryFind f.Name |> Option.defaultValue (defaultOf f.FieldType)
                                    | None -> Unknown
                                | Req tags -> Req tags
                                | _ -> Unknown

                            t <- push value rest
                        | LdFldA f ->
                            let v, rest = pop 1

                            let typeRef = { Assembly = f.Type.Assembly; FullName = elementTypeName f.FieldType }

                            let isStruct =
                                match set.TryType typeRef with
                                | Some(d, _) -> d.IsValueType && not (intTypes.Contains f.FieldType)
                                | None -> false

                            match v.Head with
                            | Ref id ->
                                match t.State.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind f.Name) with
                                | Some(Ref inner) when isStruct -> t <- push (Ref inner) rest
                                | None when isStruct ->
                                    // A struct field: give it an object so writes through the address land somewhere.
                                    let inner, s = allocate t.State typeRef None
                                    let s = update s id (fun o -> { o with Fields = o.Fields.Add(f.Name, Ref inner) })
                                    t <- { push (Ref inner) rest with State = s }
                                | _ -> t <- push (FieldAddress(id, f.Name)) rest
                            | other -> t <- push other rest
                        | StFld f ->
                            let v, rest = pop 2

                            match v with
                            | [ Ref id; value ] -> t <- { next with Stack = rest; State = update t.State id (fun o -> { o with Fields = o.Fields.Add(f.Name, value) }) }
                            | _ -> t <- { next with Stack = rest }
                        | LdSFld f
                        | LdSFldA f ->
                            match t.State.Statics.TryFind f.Key with
                            | Some v -> t <- push v t.Stack
                            | None when (matcher.Constant f).IsSome -> t <- push (Const((matcher.Constant f).Value)) t.Stack
                            | None ->
                                // First use: run the static initialization that sets the field, once per path.
                                let marker = "<init>" + f.Key

                                if t.State.Statics.ContainsKey marker || not (interpretable { me with Type = f.Type }) then
                                    t <- push Unknown t.Stack
                                else
                                    let s0 = { t.State with Statics = t.State.Statics.Add(marker, Null) }

                                    let cctor =
                                        match set.TryType f.Type with
                                        | Some(d, _) -> d.Methods |> List.tryFind (fun x -> x.Ref.Name = ".cctor") |> Option.map (fun x -> x.Ref)
                                        | None -> None

                                    let inits = (Option.toList cctor) @ (initializersOf f |> List.filter (fun x -> Some x <> cctor)) |> List.truncate 2

                                    // Run only until the field is stored, and within a budget of its own.
                                    let saved = deadline
                                    deadline <- min saved (steps + 200_000)
                                    initializing.Add f.Key |> ignore

                                    let s1 =
                                        inits
                                        |> List.fold
                                            (fun (s: PathState) init ->
                                                if s.Statics.ContainsKey f.Key then
                                                    s
                                                else
                                                    match this.Invoke(Registration, init, [], s, depth + 1) with
                                                    | (_, s2) :: _ -> s2
                                                    | [] -> s)
                                            s0

                                    initializing.Remove f.Key |> ignore
                                    deadline <- saved

                                    let v = s1.Statics.TryFind f.Key |> Option.defaultValue Unknown
                                    t <- { push v t.Stack with State = s1 }
                        | StSFld f ->
                            let v, rest = pop 1
                            t <- { next with Stack = rest; State = { t.State with Statics = t.State.Statics.Add(f.Key, v.Head) } }

                            if initializing.Contains f.Key then
                                // The field asked for is set: the rest of the initializer is not needed now.
                                outcomes.Add((Unknown, t.State))
                                running <- false
                        | LdFtn m -> t <- push (Fn(m, None)) t.Stack
                        | LdVirtFtn m ->
                            let v, rest = pop 1

                            let target =
                                match typeName t.State v.Head with
                                | Some ty -> set.Implementation(ty, m) |> Option.defaultValue m
                                | None -> m

                            t <- push (Fn(target, Some v.Head)) rest
                        | Constrained ty -> t <- { next with Constrained = Some ty }
                        | InitObj _ ->
                            let v, rest = pop 1

                            match v.Head with
                            | Ref id -> t <- { next with Stack = rest; State = update t.State id (fun o -> { o with Fields = Map.empty }) }
                            | _ -> t <- { next with Stack = rest }
                        | Box ty ->
                            // A boxed enum remembers its type, so that ToString can name its value.
                            let v, rest = pop 1

                            match set.TryType ty, v.Head with
                            | Some(d, _), (Const _ as value) when d.BaseType |> Option.exists (fun b -> b.FullName = "System.Enum") ->
                                let id, s = allocate t.State ty None
                                let s = update s id (fun o -> { o with Fields = o.Fields.Add("value__", value) })
                                t <- { push (Ref id) rest with State = s }
                            | _ -> t <- push v.Head rest
                        | UnboxAny _ ->
                            let v, rest = pop 1

                            match v.Head with
                            | Ref id when (t.State.Heap.TryFind id |> Option.bind (fun o -> o.Fields.TryFind "value__")).IsSome ->
                                t <- push t.State.Heap[id].Fields["value__"] rest
                            | other -> t <- push other rest
                        | CastClass _ -> t <- next
                        | IsInst ty ->
                            let v, rest = pop 1

                            let result =
                                match v.Head with
                                | Ref _ as r ->
                                    match typeName t.State r with
                                    | Some actual when set.IsSubtypeOf(actual, ty.FullName) -> r
                                    | Some _ -> Null
                                    | None -> Unknown
                                | Null -> Null
                                | other -> other

                            t <- push result rest
                        | Br target ->
                            match jump next target with
                            | Some n -> t <- n
                            | None -> running <- false
                        | Leave target ->
                            match jump next target with
                            | Some n -> t <- { n with Stack = [] }
                            | None -> running <- false
                        | BrTrue target ->
                            let v, rest = pop 1
                            branch { t with Stack = rest } v.Head true target
                            running <- false
                        | BrFalse target ->
                            let v, rest = pop 1
                            branch { t with Stack = rest } v.Head false target
                            running <- false
                        | BrCmp(cmp, target) ->
                            let v, rest = pop 2

                            let cond =
                                match cmp, v with
                                | Eq, [ a; b ] -> equal a b
                                | NotEq, [ a; b ] -> equal a b |> fun e -> (match e with Const(:? int64 as n) -> Const(box (1L - n)) | Bool c -> Bool { c with WhenTrue = c.WhenFalse; WhenFalse = c.WhenTrue } | x -> x)
                                | _, [ Const(:? int64 as a); Const(:? int64 as b) ] ->
                                    let r =
                                        match cmp with
                                        | Lt -> a < b
                                        | Gt -> a > b
                                        | Le -> a <= b
                                        | Ge -> a >= b
                                        | _ -> false

                                    Const(box (if r then 1L else 0L))
                                | _ -> Unknown

                            branch { t with Stack = rest } cond true target
                            running <- false
                        | Switch targets ->
                            let v, rest = pop 1
                            let rest = { t with Stack = rest }

                            match v.Head with
                            | Const(:? int64 as n) when n >= 0L && int n < targets.Length -> jump rest targets[int n] |> Option.iter fork
                            | Const _ -> fork { rest with Pc = t.Pc + 1 }
                            | _ ->
                                for target in targets do
                                    jump rest target |> Option.iter fork

                                fork { rest with Pc = t.Pc + 1 }

                            running <- false
                        | Ceq ->
                            let v, rest = pop 2
                            t <- push (match v with [ a; b ] -> equal a b | _ -> Unknown) rest
                        | Cgt
                        | Clt ->
                            let v, rest = pop 2

                            let r =
                                match v with
                                | [ Const(:? int64 as a); Const(:? int64 as b) ] -> Const(box (if (if ins.Op = Cgt then a > b else a < b) then 1L else 0L))
                                | [ (Ref _ | Fn _ | Const _); Null ] when ins.Op = Cgt -> Const(box 1L)
                                | [ Null; Null ] -> Const(box 0L)
                                | _ -> Unknown

                            t <- push r rest
                        | Ret ->
                            let result = if def.Ref.ReturnsValue then (match t.Stack with v :: _ -> v | [] -> Unknown) else Unknown
                            outcomes.Add((result, t.State))
                            running <- false
                        | Throw
                        | Rethrow
                        | EndFinally -> running <- false
                        | Dup ->
                            match t.Stack with
                            | v :: _ -> t <- push v t.Stack
                            | [] -> t <- push Unknown []
                        | Pop ->
                            let _, rest = pop 1
                            t <- { next with Stack = rest }
                        | NewArr _ ->
                            let v, rest = pop 1
                            let id, s = allocate t.State { Assembly = "System.Runtime"; FullName = "System.Array" } (Some(me, ins.Offset))
                            let s = update s id (fun o -> { o with Length = (match v.Head with Const(:? int64 as n) -> Some(int n) | _ -> None) })
                            t <- { push (Ref id) rest with State = s }
                        | LdLen ->
                            let v, rest = pop 1

                            let r =
                                match v.Head with
                                | Ref id ->
                                    match t.State.Heap.TryFind id |> Option.bind (fun o -> o.Length) with
                                    | Some n -> Const(box (int64 n))
                                    | None -> Unknown
                                | Req tags when tags.Contains Segments -> Req(Set.singleton SegmentCount)
                                | _ -> Unknown

                            t <- push r rest
                        | LdElem ->
                            let v, rest = pop 2

                            let r =
                                match v with
                                | [ Ref id; Const(:? int64 as i) ] ->
                                    match t.State.Heap.TryFind id with
                                    | Some o -> o.Elements.TryFind(int i) |> Option.defaultValue Unknown
                                    | None -> Unknown
                                | [ Req tags; Const(:? int64 as i) ] when tags.Contains Segments -> Req(Set.singleton (Segment(int i)))
                                | [ Req tags; _ ] -> Req tags
                                | _ -> Unknown

                            t <- push r rest
                        | StElem ->
                            let v, rest = pop 3

                            match v with
                            | [ Ref id; Const(:? int64 as i); value ] -> t <- { next with Stack = rest; State = update t.State id (fun o -> { o with Elements = o.Elements.Add(int i, value) }) }
                            | _ -> t <- { next with Stack = rest }
                        | Other(name, _, _) when name.StartsWith "Stind" || name = "Stobj" ->
                            // A write through an address.
                            let v, rest = pop 2

                            match v with
                            | [ FieldAddress(id, field); value ] ->
                                t <- { next with Stack = rest; State = update t.State id (fun o -> { o with Fields = o.Fields.Add(field, value) }) }
                            | _ -> t <- { next with Stack = rest }
                        | Other(name, _, _) when name.StartsWith "Ldind" || name = "Ldobj" ->
                            let v, rest = pop 1
                            t <- push (deref t.State v.Head) rest
                        | Other(name, pops, pushes) ->
                            let v, rest = pop pops

                            let result =
                                if name.StartsWith "Conv" then (match v with [ x ] -> x | _ -> Unknown)
                                else Unknown

                            t <- { next with Stack = (List.replicate pushes result) @ rest }
                        | NewObj ctor ->
                            let v, rest = pop ctor.ParameterCount
                            let isDelegate = set.BaseChain ctor.Type |> List.exists (fun b -> b.FullName = "System.MulticastDelegate")

                            if isDelegate then
                                let fnValue =
                                    match v with
                                    | [ target; Fn(m, _) ] -> Fn(m, (match target with Null -> None | x -> Some x))
                                    | [ _; f ] -> f
                                    | _ -> Unknown

                                t <- push fnValue rest
                            else
                                let id, s = allocate t.State ctor.Type (Some(me, ins.Offset))

                                if interpretable ctor then
                                    let results = this.Invoke(phase, ctor, Ref id :: v, s, depth + 1)

                                    continueWith (results |> List.map (fun (_, s2) -> Ref id, s2)) rest true
                                else
                                    // A framework object: keep its constructor arguments as fields.
                                    let s =
                                        update s id (fun o ->
                                            { o with
                                                Fields = v |> List.indexed |> List.fold (fun (fs: Map<string, SVal>) (i, a) -> fs.Add($"arg{i}", a)) o.Fields })

                                    match matcher.Registration ctor with
                                    | Some rule when phase = Registration ->
                                        let roleArg (role: string) =
                                            rule.Payload |> List.tryFind (fun p -> p.Role = role) |> Option.bind (fun p -> List.tryItem p.Argument v)

                                        match roleArg "handler", roleArg "route-template" with
                                        | Some handler, Some template ->
                                            registered.Add(Endpoint(rule, template, Some [], None, handler, Some id, s, (me, ins.Offset)))
                                        | _ -> ()
                                    | _ -> ()

                                    t <- { push (Ref id) rest with State = s }
                        | Call declared
                        | CallVirt declared ->
                            let count = declared.ParameterCount + (if declared.HasThis then 1 else 0)
                            let operands, rest = pop count

                            let target =
                                match operands with
                                | receiver :: _ when declared.HasThis ->
                                    match receiver with
                                    | Fn(m, fnTarget) when declared.Name = "Invoke" -> Choice2Of2(m, fnTarget)
                                    | _ ->
                                        match typeName t.State receiver with
                                        | Some actual ->
                                            match set.Implementation(actual, declared) with
                                            | Some impl -> Choice1Of2 impl
                                            | None -> Choice1Of2 declared
                                        | None -> Choice1Of2 declared
                                | _ -> Choice1Of2 declared

                            let savedUserSite = currentUserSite

                            if isUser me then
                                currentUserSite <- Some(me, ins.Offset)

                            let results =
                                match target with
                                | Choice2Of2(m, fnTarget) ->
                                    let args = operands.Tail
                                    let all = if m.HasThis then (defaultArg fnTarget Unknown) :: args else args
                                    this.Dispatch(phase, m, all, t.State, depth, Some(me, ins.Offset))
                                | Choice1Of2 m -> this.Dispatch(phase, m, operands, t.State, depth, Some(me, ins.Offset))

                            currentUserSite <- savedUserSite

                            continueWith results rest declared.ReturnsValue
                        | LdToken -> t <- push Unknown t.Stack

        List.ofSeq outcomes

    /// The items a sequence value produces, by running its own enumerator (or reading a modelled collection).
    member this.Enumerate(v: SVal, state: PathState) : SVal list option =
        match this.ElementsOf(state, v) with
        | Some items -> Some items
        | None ->
            match v with
            | Ref id ->
                match state.Heap.TryFind id with
                | Some o ->
                    let getEnumerator =
                        set.BaseChain o.Type
                        |> List.tryPick (fun t ->
                            match set.TryType t with
                            | Some(d, _) -> d.Methods |> List.tryFind (fun x -> shortName x.Ref.Name = "GetEnumerator" && x.Body.Value.IsSome && x.Ref.ParameterCount = 0)
                            | None -> None)

                    trace (sprintf "enumerate %s: GetEnumerator %A" o.Type.FullName (getEnumerator |> Option.map (fun g -> g.Ref.Display)))

                    match getEnumerator with
                    | Some ge ->
                        let saved = deadline
                        deadline <- min saved (steps + 100_000)

                        let started = this.Invoke(Registration, ge.Ref, [ v ], state, 0)
                        trace (sprintf "enumerate: enumerator %A" (started |> List.map fst))

                        let result =
                            match started with
                            | (Ref e, s) :: _ ->
                                let call (name: string) (s: PathState) =
                                    match s.Heap.TryFind e with
                                    | Some eo ->
                                        let target =
                                            set.BaseChain eo.Type
                                            |> List.tryPick (fun t ->
                                                match set.TryType t with
                                                | Some(d, _) -> d.Methods |> List.tryFind (fun x -> shortName x.Ref.Name = name && x.Body.Value.IsSome)
                                                | None -> None)

                                        match target with
                                        | Some m -> this.Invoke(Registration, m.Ref, [ Ref e ], s, 0) |> List.tryHead
                                        | None -> None
                                    | None -> None

                                let rec loop (s: PathState) (acc: SVal list) (n: int) =
                                    if n > 32 then
                                        None
                                    else
                                        let moved = call "MoveNext" s
                                        trace (sprintf "enumerate: MoveNext -> %A" (moved |> Option.map fst))

                                        match moved with
                                        | Some(Const(:? int64 as more), s2) when more = 0L -> Some(List.rev acc)
                                        | Some(Const _, s2) ->
                                            match call "get_Current" s2 with
                                            | Some(item, s3) -> loop s3 (item :: acc) (n + 1)
                                            | None -> None
                                        | _ -> None

                                loop s [] 0
                            | _ -> None

                        deadline <- saved
                        result
                    | None -> None
                | None -> None
            | _ -> None

    /// Runs a request root with request data as its input, collecting the handlers reached.
    member this.Explore(root: SVal, state: PathState, seed: Predicate list) =
        // Every request root gets its own budget.
        steps <- 0
        deadline <- limits.Steps
        let before = hits.Count
        let state = { state with Predicates = seed; LastUserObject = None; Uncertain = false }
        let context = Req(Set.singleton HttpContext)

        let argsFor (m: MethodRef) =
            List.replicate m.ParameterCount Unknown
            |> List.mapi (fun i _ -> if i = 0 then context else Unknown)

        let results =
            match root with
            | Fn(m, _) -> this.CallValue(RequestPhase, root, argsFor m, state, 0)
            | Ref _ -> this.CallValue(RequestPhase, root, [ context ], state, 0)
            | _ -> []

        // Paths that finished without reaching user code: the user code that built the last function object.
        for (_, s) in results do
            match s.LastUserObject with
            | Some(siteMethod, offset) when not s.Predicates.IsEmpty || not seed.IsEmpty ->
                hits.Add
                    { Handler = HandlerSite(siteMethod, offset)
                      Predicates = if s.Uncertain then [] else s.Predicates }
            | _ -> ()

        hits |> Seq.skip before |> Seq.toList

let emptyState =
    { Heap = Map.empty
      Statics = Map.empty
      Predicates = []
      LastUserObject = None
      Uncertain = false }
