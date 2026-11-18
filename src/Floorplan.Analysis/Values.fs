/// Best-effort constant propagation for payloads (plan section 5, step 5): a literal is shown as is, a value
/// built from literals and unknowns is shown as a template, anything else is "dynamic".
module Floorplan.Analysis.Values

open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow

type Symbolic =
    | Text of string
    | Hole
    /// The incoming value of argument `index` of the method (0 is `this`), then a property or field path into it.
    /// Unknown in the method itself, so shown as a hole; a caller can fill it in (docs/library-payloads-plan.md).
    | Param of index: int * members: string list
    /// A sequence of parts, concatenated.
    | Parts of Symbolic list

let rec private flatten (s: Symbolic) : Symbolic list =
    match s with
    | Parts ps -> ps |> List.collect flatten
    | other -> [ other ]

let private render (s: Symbolic) =
    flatten s
    |> List.map (function
        | Text t -> t
        | _ -> "{…}")
    |> String.concat ""

/// One piece of a payload as a dependency method sees it (docs/library-payloads-plan.md): text fixed in the
/// library, something it cannot follow, or a value its caller passes.
type Segment =
    | Literal of string
    | Unknown
    /// Argument `index` of the method (0 is `this` for instance methods), then the property or field path into it.
    | FromParameter of index: int * members: string list

/// A payload in terms of a method's arguments: its segments, concatenated.
type Origin = Segment list

module Origin =
    let unknown: Origin = [ Unknown ]

    let ofSymbolic (s: Symbolic) : Origin =
        flatten s
        |> List.map (function
            | Text t -> Literal t
            | Param(i, members) -> FromParameter(i, members)
            | _ -> Unknown)

/// The platform type C# (10 and later) builds interpolated strings with.
let private interpolationHandler = "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler"

let isTransparent (m: MethodRef) =
    // Calls that return their (first) argument's text unchanged, as far as a reader is concerned.
    match m.Type.FullName, m.Name with
    | "System.Uri", ".ctor" -> m.ParameterTypes = [ "System.String" ]
    | "System.Uri", "ToString" -> true
    | "System.String", "Trim" -> true
    | "System.String", "ToString" -> true
    | "Microsoft.AspNetCore.Http.PathString", "op_Implicit" -> true
    | "Microsoft.AspNetCore.Http.PathString", ".ctor" -> true
    | "Microsoft.AspNetCore.Http.PathString", "ToString" -> true
    | "Microsoft.AspNetCore.Http.PathString", "get_Value" -> true
    | _ -> false

/// The constant a parameterless method returns, when its body is nothing but `ldstr; ret` (a module-level
/// literal binding read through its getter, for example).
let constantReturn (set: AssemblySet) (m: MethodRef) : string option =
    if m.ParameterCount > 0 || set.Origin m.Type.Assembly = Framework then
        None
    else
        match set.TryMethod m with
        | Some(def, _) ->
            match def.Body.Value with
            | Some body ->
                match body.Instructions |> Array.filter (fun i -> i.Op <> Nop) |> Array.map (fun i -> i.Op) with
                | [| LdStr s; Ret |] -> Some s
                | _ -> None
            | None -> None
        | None -> None

/// The property a field holds: compiler-generated backing fields are named after their property (C#
/// `<Name>k__BackingField`, F# `Name@`).
let memberOfField (name: string) =
    if name.StartsWith "<" && name.Contains ">k__BackingField" then name.Substring(1, name.IndexOf '>' - 1)
    elif name.EndsWith "@" && name.Length > 1 then name.TrimEnd '@'
    else name

/// The property an instance getter reads.
let private getterName (m: MethodRef) =
    if m.HasThis && m.ParameterCount = 0 && m.ReturnsValue && m.Name.StartsWith "get_" then Some(m.Name.Substring 4) else None

/// The last store to a member found in a method body.
type private Store =
    /// A setter or field store, with the value stored.
    | Stored of Values
    /// A constructor run on the object, at this offset.
    | Constructed of MethodRef * int

/// `produced m name`: member `name` of the object method `m` produces (a constructor: the new object; another
/// method: what it returns), in terms of `m`'s arguments. `Producers` reads it from `m`'s body.
type Evaluator(flow: Flow, recognizers: ICodegenRecognizer list, constant: MethodRef -> string option, produced: MethodRef -> string -> Origin) =
    let rec value (depth: int) (values: Values) : Symbolic =
        if depth > 12 then
            Hole
        else
            match Set.toList values with
            | [ v ] -> one depth v
            | _ -> Hole

    and one (depth: int) (v: Value) : Symbolic =
        match v with
        | Pushed offset ->
            match flow.InstructionAt offset with
            | None -> Hole
            | Some ins ->
                let operands = flow.OperandsAt offset

                match ins.Op with
                | LdStr s -> Text s
                | LdNull -> Text ""
                | NewArr _ -> arrayElements depth (Set.singleton (Pushed offset))
                | Call m
                | CallVirt m when m.Type.FullName = "System.String" && m.Name = "Concat" ->
                    match operands with
                    | [ single ] when m.ParameterTypes = [ "System.String[]" ] || m.ParameterTypes = [ "System.Object[]" ] ->
                        arrayElements depth single
                    | _ -> Parts(operands |> List.map (value (depth + 1)))
                | Call m when m.Type.FullName = "System.String" && m.Name = "Format" && not operands.IsEmpty ->
                    match value (depth + 1) operands.Head with
                    | Text format ->
                        formatHoles (Text(System.Text.RegularExpressions.Regex.Replace(format, @"\{\d+(,[^}]*)?(:[^}]*)?\}", "\u0000")))
                    | _ -> Hole
                | Call m when m.Type.FullName = "System.IO.Path" && m.Name = "Combine" ->
                    let parts =
                        match operands with
                        | [ single ] when m.ParameterTypes = [ "System.String[]" ] -> flatten (arrayElements depth single)
                        | _ -> operands |> List.map (value (depth + 1))

                    parts |> List.reduce (fun a b -> Parts [ a; Text "/"; b ])
                | Call m
                | CallVirt m
                | NewObj m when isTransparent m && not operands.IsEmpty ->
                    // The text is the first operand: `this` for instance calls, the argument otherwise.
                    value (depth + 1) operands.Head
                | Call m when m.Type.FullName = interpolationHandler && m.Name = "ToStringAndClear" ->
                    interpolated depth offset operands
                | Call m when (constant m).IsSome -> Text (constant m).Value
                | Call m
                | CallVirt m when (getterName m).IsSome && not operands.IsEmpty ->
                    match memberValue (depth + 1) operands.Head (getterName m).Value offset with
                    | Hole -> formatted depth offset
                    | s -> s
                | LdFld f
                | LdFldA f when not operands.IsEmpty -> memberValue (depth + 1) operands.Head (memberOfField f.Name) offset
                // An argument's address (a struct argument's method called on it) stands for the argument, a cast
                // for the value cast (Dapper's TrySetupAsyncCommand returns `SetupCommand(...) as DbCommand`).
                | LdArgA n -> Param(n, [])
                | CastClass _
                | IsInst _ when not operands.IsEmpty -> value (depth + 1) operands.Head
                | Call _
                | CallVirt _
                | NewObj _ -> formatted depth offset
                | _ -> Hole
        | Argument i -> Param(i, [])
        | _ -> Hole

    /// A string built from a format literal somewhere in this expression (printf-style).
    and formatted (depth: int) (offset: int) : Symbolic =
        match formatLiteral (depth + 1) offset with
        | Some t -> t
        | None -> Hole

    /// The last store to member `name` of a value before `before`: a setter call or a field store (its value), or a
    /// constructor run on it (a local struct, or a chained or base constructor on `this`; one the producers know
    /// nothing about, such as System.Object's, stores nothing).
    and lastStore (receiver: Values) (name: string) (before: int) : Store option =
        match identity 0 receiver with
        | None -> None
        | Some receiverIdentity ->
            let isReceiver (target: Values) = identity 0 target = Some receiverIdentity

            flow.Body.Instructions
            |> Array.filter (fun i -> i.Offset < before)
            |> Array.choose (fun i ->
                match i.Op, flow.OperandsAt i.Offset with
                | (Call m | CallVirt m), [ target; v ] when m.Name = "set_" + name && isReceiver target -> Some(Stored v)
                | StFld f, [ target; v ] when memberOfField f.Name = name && isReceiver target -> Some(Stored v)
                | Call m, target :: _ when m.Name = ".ctor" && isReceiver target && produced m name <> Origin.unknown ->
                    Some(Constructed(m, i.Offset))
                | _ -> None)
            |> Array.tryLast

    /// Member `name` of a value, as read at `before`: the last store to it in this method, or else what made the
    /// value sets: the constructor or method that produced it, or, for a value read from a member of another
    /// value (a field, a field's address, a getter), what was stored into that member.
    and memberValue (depth: int) (receiver: Values) (name: string) (before: int) : Symbolic =
        if depth > 12 then
            Hole
        else
            match lastStore receiver name before with
            | Some(Stored v) -> value (depth + 1) v
            | Some(Constructed(ctor, offset)) -> produce depth ctor name offset false
            | None ->
                match Set.toList receiver with
                | [ Argument i ] -> Param(i, [ name ])
                | [ Pushed o ] ->
                    let held =
                        match flow.InstructionAt o, flow.OperandsAt o with
                        | Some { Op = (LdFld f | LdFldA f) }, [ owner ] -> Some(owner, memberOfField f.Name)
                        | Some { Op = (Call m | CallVirt m) }, [ owner ] when (getterName m).IsSome -> Some(owner, (getterName m).Value)
                        | _ -> None

                    match held |> Option.bind (fun (owner, field) -> lastStore owner field o) with
                    | Some(Stored v) -> memberValue (depth + 1) v name before
                    | Some(Constructed _)
                    | None ->
                        match flow.InstructionAt o with
                        | Some { Op = (CastClass _ | IsInst _) } when not (flow.OperandsAt o).IsEmpty ->
                            memberValue (depth + 1) (flow.OperandsAt o).Head name before
                        | Some { Op = NewObj ctor } -> produce depth ctor name o true
                        | Some { Op = (Call m | CallVirt m) } when m.ReturnsValue && (getterName m).IsNone -> produce depth m name o false
                        | _ ->
                            match one (depth + 1) (Pushed o) with
                            | Param(i, path) -> Param(i, path @ [ name ])
                            | _ -> Hole
                | _ -> Hole

    /// What object a value is, for matching stores to reads: a local by number (its address is taken anew at each
    /// use), a field of an object by field and object (a local hoisted into a state machine is loaded anew at
    /// each use), or else the value itself.
    and identity (depth: int) (values: Values) : string option =
        if depth > 8 then
            None
        else
            match Set.toList values with
            | [ LocalAddress(n, _) ] -> Some $"local {n}"
            | [ Pushed o ] ->
                match flow.InstructionAt o, flow.OperandsAt o with
                | Some { Op = (LdFld f | LdFldA f) }, [ owner ] -> identity (depth + 1) owner |> Option.map (fun i -> $"{i}.{f.Name}")
                | _ -> Some $"pushed {o}"
            | [ v ] -> Some(string v)
            | _ -> None

    /// Member `name` of what the call at `offset` produces.
    and produce (depth: int) (m: MethodRef) (name: string) (offset: int) (isNewObj: bool) : Symbolic =
        match produced m name with
        | [ Unknown ] -> Hole
        | origin -> resolveAt depth origin offset isNewObj

    /// A callee's origin evaluated at the call at `offset`: its arguments replaced by what is passed there.
    and resolveAt (depth: int) (origin: Origin) (offset: int) (isNewObj: bool) : Symbolic =
        let operands = flow.OperandsAt offset

        origin
        |> List.map (function
            | Literal t -> Text t
            | Unknown -> Hole
            | FromParameter(i, path) ->
                // `this` of a constructor is not on the stack.
                let index = if isNewObj then i - 1 else i

                match List.tryItem index operands with
                | Some v when index >= 0 -> memberPath depth v path offset
                | _ -> Hole)
        |> function
            | [ single ] -> single
            | parts -> Parts parts

    and memberPath (depth: int) (v: Values) (path: string list) (before: int) : Symbolic =
        match path with
        | [] -> value (depth + 1) v
        | first :: rest ->
            match memberValue (depth + 1) v first before, rest with
            | s, [] -> s
            | Param(i, p), _ -> Param(i, p @ rest)
            | _ -> Hole

    /// An interpolated string built with the platform's handler on a local:
    /// `ldloca h; call .ctor; ldloca h; ldstr; call AppendLiteral; ldloca h; ...; call AppendFormatted; ...;
    /// ldloca h; call ToStringAndClear`. The parts are the appends to that local since its last construction.
    and interpolated (depth: int) (offset: int) (operands: Values list) : Symbolic =
        let localOf (vs: Values) =
            match Set.toList vs with
            | [ LocalAddress(n, _) ] -> Some n
            | _ -> None

        match operands |> List.tryHead |> Option.bind localOf with
        | None -> Hole
        | Some local ->
            let calls =
                flow.Body.Instructions
                |> Array.filter (fun i -> i.Offset < offset)
                |> Array.choose (fun i ->
                    match i.Op with
                    | Call m when m.Type.FullName = interpolationHandler ->
                        match flow.OperandsAt i.Offset with
                        | target :: args when localOf target = Some local -> Some(m.Name, args)
                        | _ -> None
                    | _ -> None)

            let since =
                match calls |> Array.tryFindIndexBack (fun (name, _) -> name = ".ctor") with
                | Some i -> calls[i + 1 ..]
                | None -> calls

            if since.Length = 0 then
                Hole
            else
                since
                |> Array.toList
                |> List.map (fun (name, args) ->
                    match name, args with
                    | "AppendLiteral", [ text ] -> value (depth + 1) text
                    | _ -> Hole)
                |> Parts

    and formatHoles (t: Symbolic) =
        match t with
        | Text s -> Parts(s.Split('\u0000') |> Array.toList |> List.map Text |> List.reduce (fun a b -> Parts [ a; Hole; b ]) |> List.singleton)
        | other -> other

    /// Elements stored into a freshly created array: newarr; dup; ldc i; value; stelem ...
    and arrayElements (depth: int) (array: Values) : Symbolic =
        match Set.toList array with
        | [ Pushed newarr ] ->
            let stores =
                flow.Body.Instructions
                |> Array.filter (fun i -> i.Op = StElem)
                |> Array.choose (fun i ->
                    match flow.OperandsAt i.Offset with
                    | [ arr; index; element ] when arr.Contains(Pushed newarr) ->
                        let idx =
                            match Set.toList index with
                            | [ Pushed o ] ->
                                match flow.InstructionAt o with
                                | Some { Op = LdcI4 n } -> Some n
                                | _ -> None
                            | _ -> None

                        idx |> Option.map (fun n -> n, value (depth + 1) element)
                    | _ -> None)
                |> Array.sortBy fst

            if stores.Length = 0 then Hole else Parts(stores |> Array.map snd |> Array.toList)
        | _ -> Hole

    and formatLiteral (depth: int) (offset: int) : Symbolic option =
        if depth > 6 then
            None
        else
            match flow.InstructionAt offset with
            | Some { Op = (Call m | CallVirt m | NewObj m) } ->
                let direct =
                    recognizers
                    |> List.tryPick (fun r -> r.FormatArgument m |> Option.map (fun i -> r, i))
                    |> Option.bind (fun (r, i) ->
                        match List.tryItem i (flow.OperandsAt offset) with
                        | Some arg ->
                            match value (depth + 1) arg with
                            | Text format ->
                                // Holes in the rendered template become holes in the value.
                                r.FormatTemplate(format).Split("{…}")
                                |> Array.toList
                                |> List.mapi (fun i piece -> if i = 0 then [ Text piece ] else [ Hole; Text piece ])
                                |> List.concat
                                |> Parts
                                |> Some
                            | _ -> None
                        | None -> None)

                match direct with
                | Some t -> Some t
                | None ->
                    flow.OperandsAt offset
                    |> List.tryPick (fun vs ->
                        match Set.toList vs with
                        | [ Pushed o ] -> formatLiteral (depth + 1) o
                        | _ -> None)
            | _ -> None

    new(flow, recognizers, constant) = Evaluator(flow, recognizers, constant, (fun _ _ -> Origin.unknown))

    /// The symbolic value of operand `index` (in push order) of the instruction at `offset`.
    member _.Operand(offset: int, index: int) : Symbolic =
        match List.tryItem index (flow.OperandsAt offset) with
        | Some v -> value 0 v
        | None -> Hole

    member _.Value(values: Values) = value 0 values

    /// As a payload: literal, template or dynamic.
    member this.Payload(offset: int, index: int, role: string) : Payload = Evaluator.ToPayload(this.Operand(offset, index), role)

    /// A property of the call's receiver as a payload: the value last stored through `set_<property>` on the same
    /// object before the call, or else the string constructor argument at `constructorArgument` of the
    /// construction that made it. Anything else is dynamic.
    member this.PropertyPayload(offset: int, property: string, constructorArgument: int option, role: string) : Payload =
        Evaluator.ToPayload(this.ReceiverMember(offset, property, constructorArgument), role)

    /// A property of the call's receiver at the call: as `memberValue` finds it, or else the string constructor
    /// argument at `constructorArgument` of the construction that made the receiver.
    member this.ReceiverMember(offset: int, property: string, constructorArgument: int option) : Symbolic =
        this.OperandMember(offset, 0, property, constructorArgument)

    /// A property of operand `index` of the call at `offset` (a request's URL): as `ReceiverMember` finds it for the
    /// receiver. The constructor argument fallback takes a string or a `Uri` parameter.
    member this.OperandMember(offset: int, index: int, property: string, constructorArgument: int option) : Symbolic =
        match List.tryItem index (flow.OperandsAt offset) with
        | None -> Hole
        | Some operand ->
            match memberValue 0 operand property offset, Set.toList operand with
            | Hole, [ Pushed o ] ->
                match flow.InstructionAt o, constructorArgument with
                | Some { Op = NewObj ctor }, Some i when
                    (match List.tryItem i ctor.ParameterTypes with
                     | Some "System.String"
                     | Some "System.Uri" -> true
                     | _ -> false)
                    ->
                    this.Operand(o, i)
                | _ -> Hole
            | found, _ -> found

    /// Member `name` of a value read at `before`, in terms of this method's arguments.
    member _.MemberOf(receiver: Values, name: string, before: int) : Origin =
        Origin.ofSymbolic (memberValue 0 receiver name before)

    /// What a value is in terms of this method's arguments.
    member _.OriginOf(values: Values) : Origin = Origin.ofSymbolic (value 0 values)

    /// The origin of a state machine's body (an async method's or iterator's MoveNext, in terms of its `this`)
    /// seen from the method that starts it: the machine's fields are what this method stores into them. The
    /// machine is the object constructed at `offset`, or this method's local of type `machine` (a struct
    /// machine, started through the platform's builder). `alternatives` names other fields the argument held in
    /// a field may be stored into. With `constructedOnly` (function objects, such as F# closures and sequence
    /// expressions), only an object constructed at `offset` counts.
    member _.ThroughStateMachine(callee: Origin, offset: int, machine: TypeRef, alternatives: string -> string list, constructedOnly: bool) : Origin =
        let receiver =
            match flow.InstructionAt offset with
            | Some { Op = NewObj ctor } when ctor.Type.FullName = machine.FullName -> Some(Set.singleton (Pushed offset))
            | _ when constructedOnly -> None
            | Some { Op = InitObj t } when t.FullName = machine.FullName -> flow.OperandsAt offset |> List.tryHead
            | _ ->
                flow.Body.LocalTypes
                |> List.tryFindIndex (fun t -> t.FullName = machine.FullName)
                |> Option.map (fun n -> Set.singleton (LocalAddress(n, 0)))

        match receiver with
        | None -> Origin.unknown
        | Some machineValue ->
            callee
            |> List.map (function
                | Literal t -> Text t
                | FromParameter(0, field :: rest) ->
                    field :: alternatives field
                    |> List.tryPick (fun name ->
                        match memberPath 0 machineValue (name :: rest) System.Int32.MaxValue with
                        | Hole -> None
                        | found -> Some found)
                    |> Option.defaultValue Hole
                | _ -> Hole)
            |> Parts
            |> Origin.ofSymbolic

    /// A callee's origin seen from the call at `offset`: its arguments replaced by what this method passes.
    member this.Substitute(callee: Origin, offset: int, isNewObj: bool) : Origin =
        Origin.ofSymbolic (this.Resolve(callee, offset, isNewObj))

    /// A callee's origin evaluated at the call at `offset`, as a value: what the user's call site passes.
    member _.Resolve(callee: Origin, offset: int, isNewObj: bool) : Symbolic = resolveAt 0 callee offset isNewObj

    static member ToPayload(value: Symbolic, role: string) : Payload =
        match value with
        | Text t ->
            { Role = role
              Kind = PayloadKind.Literal
              Value = Some t }
        | Hole ->
            { Role = role
              Kind = PayloadKind.Dynamic
              Value = None }
        | parts when flatten parts |> List.forall (function Text _ -> true | _ -> false) ->
            // Only text, joined: a literal.
            { Role = role
              Kind = PayloadKind.Literal
              Value = Some(render parts) }
        | parts ->
            let rendered = render parts

            if rendered.Replace("{…}", "").Trim() = "" then
                { Role = role
                  Kind = PayloadKind.Dynamic
                  Value = None }
            else
                { Role = role
                  Kind = PayloadKind.Template
                  Value = Some rendered }

/// The operand index of declared argument `argument` of a call: `this` comes first for instance calls.
let operandIndex (m: MethodRef) (isNewObj: bool) (argument: int) =
    if m.HasThis && not isNewObj then argument + 1 else argument

/// What methods outside the one being evaluated set on the objects they produce (`Evaluator`'s `produced`): a
/// constructor's stores to the new object, a method's to the object it returns. Read once per (method, member)
/// from the method's body; base-library methods are not read.
type Producers(set: AssemblySet, recognizers: ICodegenRecognizer list) =
    let memo = System.Collections.Generic.Dictionary<struct (string * string), Origin>()

    member this.Member(m: MethodRef, name: string) : Origin =
        let key = struct (m.Key, name)

        match memo.TryGetValue key with
        | true, o -> o
        | _ ->
            // Unknown while computed, so recursion through the same member ends.
            memo[key] <- Origin.unknown

            let origin =
                if set.Origin m.Type.Assembly = Framework then
                    Origin.unknown
                else
                    match set.TryMethod m |> Option.bind (fun (def, _) -> DataFlow.ofMethod def) with
                    | None -> Origin.unknown
                    | Some flow ->
                        let evaluator = Evaluator(flow, recognizers, constantReturn set, fun m n -> this.Member(m, n))

                        if m.Name = ".ctor" then
                            evaluator.MemberOf(Set.singleton (Argument 0), name, System.Int32.MaxValue)
                        else
                            let returns =
                                flow.Body.Instructions
                                |> Array.filter (fun i -> i.Op = Ret)
                                |> Array.choose (fun i ->
                                    match flow.OperandsAt i.Offset with
                                    | [ v ] -> Some(i.Offset, v)
                                    | _ -> None)

                            match returns with
                            | [| offset, v |] -> evaluator.MemberOf(v, name, offset)
                            | _ -> Origin.unknown

            memo[key] <- origin
            origin
