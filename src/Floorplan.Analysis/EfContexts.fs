/// Entity Framework Core contexts (docs/landscape-plan.md): the context type a database call is made on, and the
/// one a connection string is given to, so each call can be sent to its context's database when a solution has
/// several. Read from the code only: what the call's arguments are made from, and the lambdas that registering a
/// context (AddDbContext<T>(o => o.UseSqlServer(...))) is given.
module Floorplan.Analysis.EfContexts

open System.Collections.Generic
open Floorplan.IL

[<Literal>]
let private DbContext = "Microsoft.EntityFrameworkCore.DbContext"

/// The methods that register a context with the options lambda they are given, by name, on
/// EntityFrameworkServiceCollectionExtensions.
let private registrations =
    set [ "AddDbContext"; "AddDbContextPool"; "AddDbContextFactory"; "AddPooledDbContextFactory" ]

[<Literal>]
let private Extensions = "Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions"

type Contexts(set: AssemblySet, assembly: AssemblyDef) =
    let known = Dictionary<string, bool>()

    /// Whether a type, by its full name, is a context of the application's: derives from DbContext.
    let isContext (name: string) =
        if System.String.IsNullOrEmpty name || name = DbContext || name.Contains '<' then
            false
        else
            match known.TryGetValue name with
            | true, b -> b
            | _ ->
                let b = set.IsSubtypeOf({ Assembly = ""; FullName = name }, DbContext)
                known[name] <- b
                b

    // The methods a delegate among the values is made from: ldftn, through newobj, dup, and the static field a
    // C# compiler caches a lambda that captures nothing in.
    let rec targets (f: DataFlow.Flow) (depth: int) (values: DataFlow.Values) : MethodRef list =
        if depth > 6 then
            []
        else
            values
            |> Set.toList
            |> List.collect (function
                | DataFlow.Pushed o ->
                    match f.InstructionAt o with
                    | Some { Op = (LdFtn m | LdVirtFtn m) } -> [ m ]
                    | Some { Op = (NewObj _ | Dup) } -> f.OperandsAt o |> List.collect (targets f (depth + 1))
                    | Some { Op = LdSFld fld } ->
                        f.Body.Instructions
                        |> Array.toList
                        |> List.collect (fun ins ->
                            match ins.Op with
                            | StSFld g when g.Key = fld.Key -> f.OperandsAt ins.Offset |> List.collect (targets f (depth + 1))
                            | _ -> [])
                    | _ -> []
                | _ -> [])

    /// The lambdas given to a context's registration, each with the context: AddDbContext<T>(o => ...).
    let configured =
        lazy
            (let found = Dictionary<string, string>()

             for t in assembly.Types do
                 for m in t.Methods do
                     match m.Body.Value with
                     | None -> ()
                     | Some body ->
                         let calls =
                             body.Instructions
                             |> Array.filter (fun ins ->
                                 match ins.Op with
                                 | Call d
                                 | CallVirt d -> d.Type.FullName = Extensions && registrations.Contains d.Name
                                 | _ -> false)

                         if calls.Length > 0 then
                             match DataFlow.ofMethod m with
                             | None -> ()
                             | Some f ->
                                 for ins in calls do
                                     match ins.Op with
                                     | Call d
                                     | CallVirt d ->
                                         // AddDbContext<TService, TImplementation>: the implementation is the context.
                                         match d.TypeArguments |> List.rev |> List.tryFind (fun a -> isContext a.FullName) with
                                         | Some context ->
                                             for target in f.OperandsAt ins.Offset |> List.collect (targets f 0) do
                                                 found[target.Key] <- context.FullName
                                         | None -> ()
                                     | _ -> ()

             found)

    let flows = Dictionary<string, (MethodDef * DataFlow.Flow) option>()

    // The application's own method a call runs, and its flow.
    let userFlow (c: MethodRef) =
        match flows.TryGetValue c.Key with
        | true, found -> found
        | _ ->
            let found =
                if not (set.IsUser c.Type.Assembly) then None
                else
                    match set.TryMethod c with
                    | Some(def, _) -> DataFlow.ofMethod def |> Option.map (fun f -> def, f)
                    | None -> None

            flows[c.Key] <- found
            found

    // The contexts a value is made from: a field, parameter or call result of a context's type, a member of a
    // context (db.Orders, db.Database), what a call was made on or given (db.Orders.Where(...)), or what an
    // application's method it calls returns (a repository's All() => db.Orders).
    let rec contextsOf (m: MethodDef) (f: DataFlow.Flow) (depth: int) (values: DataFlow.Values) : string list =
        if depth > 12 then
            []
        else
            values
            |> Set.toList
            |> List.collect (function
                | DataFlow.Argument i ->
                    let name =
                        if m.Ref.HasThis && i = 0 then Some m.Ref.Type.FullName
                        else m.Ref.ParameterTypes |> List.tryItem (if m.Ref.HasThis then i - 1 else i)

                    name |> Option.filter isContext |> Option.toList
                | DataFlow.Pushed o ->
                    match f.InstructionAt o with
                    | Some { Op = (LdFld fld | LdSFld fld) } when isContext fld.FieldType -> [ fld.FieldType ]
                    | Some { Op = (Call c | CallVirt c | NewObj c) } ->
                        if isContext c.ReturnType then [ c.ReturnType ]
                        elif c.Name <> ".ctor" && isContext c.Type.FullName then [ c.Type.FullName ]
                        else
                            match f.OperandsAt o |> List.collect (contextsOf m f (depth + 1)) with
                            | [] when c.Name <> ".ctor" ->
                                match userFlow c with
                                | Some(def, cf) ->
                                    cf.Body.Instructions
                                    |> Array.toList
                                    |> List.collect (fun ins ->
                                        match ins.Op with
                                        | Ret -> cf.OperandsAt ins.Offset |> List.collect (contextsOf def cf (depth + 1))
                                        | _ -> [])
                                | None -> []
                            | found -> found
                    | Some { Op = (CastClass _ | IsInst _ | Box _ | UnboxAny _ | Dup) } ->
                        f.OperandsAt o |> List.collect (contextsOf m f (depth + 1))
                    | _ -> []
                | _ -> [])

    // The contexts the type's own fields hold (a repository given one context).
    let fieldContexts = Dictionary<string, string list>()

    let ofFields (t: TypeRef) =
        match fieldContexts.TryGetValue t.FullName with
        | true, cs -> cs
        | _ ->
            let cs =
                match set.TryType t with
                | Some(def, _) ->
                    def.Methods
                    |> List.collect (fun m ->
                        match m.Body.Value with
                        | Some body ->
                            body.Instructions
                            |> Array.toList
                            |> List.choose (fun ins ->
                                match ins.Op with
                                | LdFld fld
                                | StFld fld when fld.Type.FullName = t.FullName && isContext fld.FieldType -> Some fld.FieldType
                                | _ -> None)
                        | None -> [])
                    |> List.distinct
                | None -> []

            fieldContexts[t.FullName] <- cs
            cs

    let single (cs: string list) =
        match List.distinct cs with
        | [ c ] -> Some c
        | _ -> None

    /// The context a database call at `offset` in `m` is made on, or the one the connection string it is given
    /// goes to, when it is one: from the call itself (AddSqlServer<T>, UseSqlServer on a DbContextOptionsBuilder<T>),
    /// the context whose OnConfiguring makes it, the registration whose lambda it is in, what its arguments are
    /// made from, or else the one context the type's fields hold.
    member _.At(m: MethodDef, flow: DataFlow.Flow option, offset: int, declared: MethodRef) : string option =
        let fromCall =
            declared.TypeArguments @ declared.DeclaringTypeArguments |> List.map (fun a -> a.FullName) |> List.filter isContext |> single

        let owner (t: TypeRef) =
            // A lambda's or state machine's method is in a type nested in the one that wrote it.
            let rec walk (t: TypeRef) (depth: int) =
                if isContext t.FullName then Some t.FullName
                elif depth > 3 then None
                else
                    match set.TryType t with
                    | Some(def, _) -> def.DeclaringType |> Option.bind (fun d -> walk d (depth + 1))
                    | None -> None

            walk t 0

        fromCall
        |> Option.orElse (owner m.Ref.Type)
        |> Option.orElse (match configured.Value.TryGetValue m.Ref.Key with | true, c -> Some c | _ -> None)
        |> Option.orElse (
            flow
            |> Option.bind (fun f -> f.OperandsAt offset |> List.collect (contextsOf m f 0) |> single))
        |> Option.orElse (
            // A state machine's or lambda's fields are its method's: the type that wrote it holds the context.
            let rec outer (t: TypeRef) (depth: int) =
                match single (ofFields t) with
                | Some c -> Some c
                | None when depth < 3 ->
                    match set.TryType t with
                    | Some(def, _) -> def.DeclaringType |> Option.bind (fun d -> outer d (depth + 1))
                    | None -> None
                | None -> None

            outer m.Ref.Type 0)
