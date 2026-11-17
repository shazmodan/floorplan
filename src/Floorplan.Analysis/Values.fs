/// Best-effort constant propagation for payloads (plan section 5, step 5): a literal is shown as is, a value
/// built from literals and unknowns is shown as a template, anything else is "dynamic".
module Floorplan.Analysis.Values

open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow

type Symbolic =
    | Text of string
    | Hole
    /// The incoming value of argument `index` of the method (0 is `this`). Unknown in the method itself, so shown
    /// as a hole; a caller can fill it in (docs/library-payloads-plan.md).
    | Param of index: int
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
            | Param i -> FromParameter(i, [])
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

type Evaluator(flow: Flow, recognizers: ICodegenRecognizer list, constant: MethodRef -> string option) =
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
                | CallVirt m
                | NewObj m ->
                    // A string built from a format literal somewhere in this expression (printf-style).
                    match formatLiteral (depth + 1) offset with
                    | Some t -> t
                    | None -> Hole
                | _ -> Hole
        | Argument i -> Param i
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
        let found =
            match this.PropertySource(offset, property, constructorArgument) with
            | Some v -> value 0 v
            | None -> Hole

        Evaluator.ToPayload(found, role)

    /// The values a property of the call's receiver holds at the call, as `PropertyPayload` finds them.
    member _.PropertySource(offset: int, property: string, constructorArgument: int option) : Values option =
        match flow.OperandsAt offset |> List.tryHead |> Option.map Set.toList with
        | Some [ Pushed o ] ->
            let setter = "set_" + property

            let stored =
                flow.Body.Instructions
                |> Array.filter (fun i -> i.Offset < offset)
                |> Array.choose (fun i ->
                    match i.Op with
                    | Call m
                    | CallVirt m when m.Name = setter ->
                        match flow.OperandsAt i.Offset with
                        | [ target; v ] when target = Set.singleton (Pushed o) -> Some v
                        | _ -> None
                    | _ -> None)
                |> Array.tryLast

            match stored with
            | Some v -> Some v
            | None ->
                match flow.InstructionAt o, constructorArgument with
                | Some { Op = NewObj ctor }, Some i when List.tryItem i ctor.ParameterTypes = Some "System.String" ->
                    List.tryItem i (flow.OperandsAt o)
                | _ -> None
        | _ -> None

    /// What a value is in terms of this method's arguments.
    member _.OriginOf(values: Values) : Origin = Origin.ofSymbolic (value 0 values)

    /// The operand of a call that holds argument `index` of the called method: `this` of a constructor is not
    /// on the stack.
    static member private OperandFor(index: int, isNewObj: bool) = if isNewObj then index - 1 else index

    /// A callee's origin seen from the call at `offset`: its arguments replaced by what this method passes.
    member this.Substitute(callee: Origin, offset: int, isNewObj: bool) : Origin =
        callee
        |> List.collect (function
            | FromParameter(i, []) ->
                match List.tryItem (Evaluator.OperandFor(i, isNewObj)) (flow.OperandsAt offset) with
                | Some v when i >= 0 -> this.OriginOf v
                | _ -> Origin.unknown
            | FromParameter _ -> Origin.unknown
            | other -> [ other ])

    /// A callee's origin evaluated at the call at `offset`, as a value: what the user's call site passes.
    member this.Resolve(callee: Origin, offset: int, isNewObj: bool) : Symbolic =
        callee
        |> List.map (function
            | Literal t -> Text t
            | Unknown -> Hole
            | FromParameter(i, []) when Evaluator.OperandFor(i, isNewObj) >= 0 -> this.Operand(offset, Evaluator.OperandFor(i, isNewObj))
            | FromParameter _ -> Hole)
        |> function
            | [ single ] -> single
            | parts -> Parts parts

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
