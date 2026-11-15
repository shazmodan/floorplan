/// Which module or namespace declares a symbol, by logical F# name.
module Floorplan.Structure.FSharp.SymbolOwner

open FSharp.Compiler.Symbols

/// The logical full name of a module, e.g. "Shop.Core.Orders.Validation".
let rec moduleName (entity: FSharpEntity) : string =
    match entity.DeclaringEntity with
    | Some parent -> moduleName parent + "." + entity.LogicalName
    | None ->
        match entity.Namespace with
        | Some ns when ns <> "" -> ns + "." + entity.LogicalName
        | _ -> entity.LogicalName

/// The nearest module containing the entity, or the namespace it is declared in.
let rec ofEntity (entity: FSharpEntity) : string option =
    if entity.IsFSharpModule then
        Some(moduleName entity)
    elif entity.IsNamespace then
        // A namespace symbol (e.g. in an open declaration) names the namespace itself.
        Some(defaultArg entity.Namespace "" |> fun ns -> if ns = "" then entity.LogicalName else ns + "." + entity.LogicalName)
    else
        match entity.DeclaringEntity with
        | Some parent -> ofEntity parent
        | None -> entity.Namespace

let private tryEntity (t: FSharpType) =
    if t.HasTypeDefinition then Some t.TypeDefinition else None

/// The unit that declares the symbol, or None for symbols (locals, type parameters) that belong to no unit.
let ofSymbol (symbol: FSharpSymbol) : string option =
    try
        match symbol with
        | :? FSharpEntity as e -> ofEntity e
        | :? FSharpMemberOrFunctionOrValue as m ->
            if m.IsModuleValueOrMember || m.IsMember then
                m.DeclaringEntity |> Option.bind ofEntity
            else
                None
        | :? FSharpUnionCase as c -> c.ReturnType |> tryEntity |> Option.bind ofEntity
        | :? FSharpField as f -> f.DeclaringEntity |> Option.bind ofEntity
        | :? FSharpActivePatternCase as a -> a.Group.DeclaringEntity |> Option.bind ofEntity
        | _ -> None
    with _ ->
        // Symbols from assemblies FCS could not fully resolve.
        None

let assemblyOf (symbol: FSharpSymbol) =
    try Some symbol.Assembly.SimpleName with _ -> None
