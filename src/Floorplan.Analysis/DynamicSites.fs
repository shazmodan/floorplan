/// C# `dynamic` call sites that are known to MVC rather than blind spots (GitHub issue 2). The compiler makes a
/// binder (`Binder.SetMember(flags, "Title", ...)`), wraps it in a call site kept in a static field, and calls the
/// site's delegate with the receiver: `site.Target.Invoke(site, receiver, value)`.
module Floorplan.Analysis.DynamicSites

open Floorplan.IL
open Floorplan.IL.DataFlow

/// The static field the call site made from the binder at `binderOffset` is stored in.
let private siteField (flow: Flow) (binderOffset: int) : FieldRef option =
    let consumerOf (offset: int) =
        flow.Body.Instructions
        |> Array.tryFind (fun i -> flow.OperandsAt i.Offset |> List.exists (fun v -> v.Contains(Pushed offset)))

    match consumerOf binderOffset with
    | Some create ->
        match consumerOf create.Offset with
        | Some { Op = StSFld f } -> Some f
        | _ -> None
    | None -> None

/// The operands of each invocation of the call site kept in `field`: the site, the receiver, the arguments.
let private invocations (flow: Flow) (field: FieldRef) : Values list list =
    flow.Body.Instructions
    |> Array.choose (fun i ->
        match i.Op with
        | CallVirt d when d.Name = "Invoke" ->
            match flow.OperandsAt i.Offset with
            | _ :: site :: rest when
                site
                |> Set.exists (function
                    | Pushed o ->
                        match flow.InstructionAt o with
                        | Some { Op = LdSFld f } -> f.Key = field.Key
                        | _ -> false
                    | _ -> false)
                ->
                Some rest
            | _ -> None
        | _ -> None)
    |> Array.toList

/// Whether a value is MVC's `ViewBag` (of a controller, a view or a page).
let private isViewBag (flow: Flow) (v: Values) =
    match Set.toList v with
    | [ Pushed o ] ->
        match flow.InstructionAt o with
        | Some { Op = (Call g | CallVirt g) } -> g.Name = "get_ViewBag" && g.Type.Assembly.StartsWith "Microsoft.AspNetCore.Mvc"
        | _ -> false
    | _ -> false

/// The receiver's static type of a call site, from its field's type:
/// `CallSite<Action<CallSite, Receiver, ...>>`.
let private receiverType (field: FieldRef) : string option =
    let t = field.FieldType

    match t.IndexOf('<', t.IndexOf('<') + 1) with
    | -1 -> None
    | start ->
        // The delegate's type arguments, split at the top level.
        let args = ResizeArray<string>()
        let mutable depth = 0
        let mutable from = start + 1

        for i in start + 1 .. t.Length - 1 do
            match t[i] with
            | '<' -> depth <- depth + 1
            | '>' when depth = 0 ->
                if from >= 0 then args.Add(t.Substring(from, i - from))
                from <- -1
            | '>' -> depth <- depth - 1
            | ',' when depth = 0 && from >= 0 ->
                args.Add(t.Substring(from, i - from))
                from <- i + 1
            | _ -> ()

        if args.Count > 1 then Some(args[1].Trim()) else None

let private derivesFrom (set: AssemblySet) (assembly: string) (typeName: string) (baseName: string) =
    let rec walk (t: TypeRef option) (depth: int) =
        match t with
        | Some t when depth < 12 ->
            // A generic base (RazorPage<TModel>) by its definition.
            let t =
                match t.FullName.IndexOf '<' with
                | -1 -> t
                | i -> { t with FullName = t.FullName.Substring(0, i) }

            t.FullName = baseName
            || match set.TryType t with
               | Some(d, _) -> walk d.BaseType (depth + 1)
               | None -> false
        | _ -> false

    walk (Some { Assembly = assembly; FullName = typeName }) 0

/// Whether the binder call at `binderOffset` in method `m` makes a call site that MVC defines: a member of
/// `ViewBag` read or written (a dictionary entry of the view data), or a Razor view writing a dynamic value to the
/// page (`@ViewBag.Title` is `Write(ViewBag.Title)` with a dynamic argument). Its member name is the binder's
/// second argument.
let isMvcViewData (set: AssemblySet) (m: MethodDef) (flow: Flow) (binder: MethodRef) (binderOffset: int) : bool =
    let name =
        match flow.OperandsAt binderOffset |> List.tryItem 1 |> Option.map Set.toList with
        | Some [ Pushed o ] ->
            match flow.InstructionAt o with
            | Some { Op = LdStr s } -> Some s
            | _ -> None
        | _ -> None

    match siteField flow binderOffset with
    | None -> false
    | Some field ->
        let calls = invocations flow field

        match binder.Name with
        | "GetMember"
        | "SetMember" -> not calls.IsEmpty && calls |> List.forall (fun operands -> operands |> List.tryHead |> Option.exists (isViewBag flow))
        | "InvokeMember" when name = Some "Write" ->
            match receiverType field with
            | Some receiver -> derivesFrom set m.Ref.Type.Assembly receiver "Microsoft.AspNetCore.Mvc.Razor.RazorPageBase"
            | None -> false
        | _ -> false
