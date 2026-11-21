/// The modules a file declares, with the source range each one covers, read from the untyped syntax tree.
module Floorplan.Structure.FSharp.ModuleTree

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Floorplan.Model

type DeclaredUnit =
    { FullName: string
      Kind: UnitKind
      Parent: string option
      Range: range }

let private join (ids: LongIdent) =
    ids |> List.map (fun i -> i.idText) |> String.concat "."

/// A namespace only becomes a unit when it holds declarations of its own, not just modules.
let private hasOwnDeclarations (decls: SynModuleDecl list) =
    decls
    |> List.exists (function
        | SynModuleDecl.NestedModule _
        | SynModuleDecl.Open _
        | SynModuleDecl.HashDirective _
        | SynModuleDecl.Attributes _
        | SynModuleDecl.NamespaceFragment _ -> false
        | _ -> true)

let rec private nested (parent: string) (decls: SynModuleDecl list) =
    decls
    |> List.collect (function
        | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = ids); decls = inner; range = r) ->
            let name = parent + "." + join ids

            { FullName = name
              Kind = UnitKind.Module
              Parent = Some parent
              Range = r }
            :: nested name inner
        | _ -> [])

let ofParsedInput (input: ParsedInput) : DeclaredUnit list =
    match input with
    | ParsedInput.SigFile _ -> []
    | ParsedInput.ImplFile file ->
        file.Contents
        |> List.collect (fun (SynModuleOrNamespace(longId = ids; kind = kind; decls = decls; range = r)) ->
            let name = join ids

            match kind with
            | SynModuleOrNamespaceKind.NamedModule
            | SynModuleOrNamespaceKind.AnonModule ->
                { FullName = name
                  Kind = UnitKind.Module
                  Parent = None
                  Range = r }
                :: nested name decls
            | SynModuleOrNamespaceKind.DeclaredNamespace
            | SynModuleOrNamespaceKind.GlobalNamespace ->
                let own =
                    if hasOwnDeclarations decls && name <> "" then
                        [ { FullName = name
                            Kind = UnitKind.Namespace
                            Parent = None
                            Range = r } ]
                    else
                        []

                let modules =
                    decls
                    |> List.collect (function
                        | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = mids); decls = inner; range = mr) ->
                            let full = if name = "" then join mids else name + "." + join mids

                            { FullName = full
                              Kind = UnitKind.Module
                              Parent = None
                              Range = mr }
                            :: nested full inner
                        | _ -> [])

                own @ modules)

/// A type a unit declares, with the source range it covers.
type DeclaredType =
    { Unit: string
      Name: string
      Range: range }

/// The types each module and namespace of a file declares (not those nested in a type: F# has none).
let typesOf (input: ParsedInput) : DeclaredType list =
    let rec inDecls (unitName: string) (decls: SynModuleDecl list) =
        decls
        |> List.collect (function
            | SynModuleDecl.Types(typeDefns = defns) ->
                defns
                |> List.map (fun (SynTypeDefn(typeInfo = SynComponentInfo(longId = ids); range = r)) ->
                    { Unit = unitName; Name = join ids; Range = r })
            | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = ids); decls = inner) ->
                inDecls (if unitName = "" then join ids else unitName + "." + join ids) inner
            | _ -> [])

    match input with
    | ParsedInput.SigFile _ -> []
    | ParsedInput.ImplFile file ->
        file.Contents
        |> List.collect (fun (SynModuleOrNamespace(longId = ids; decls = decls)) -> inDecls (join ids) decls)

/// The innermost unit whose range contains the position.
let enclosing (units: DeclaredUnit list) (file: string) (pos: pos) =
    units
    |> List.filter (fun u ->
        System.String.Equals(u.Range.FileName, file, System.StringComparison.OrdinalIgnoreCase)
        && Range.rangeContainsPos u.Range pos)
    |> List.sortBy (fun u -> u.FullName.Length)
    |> List.tryLast
