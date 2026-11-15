/// The public API of reference assemblies in the namespaces the catalog watches (plan section 8.3).
module CatalogDiff.ApiSurface

open System
open System.IO
open Mono.Cecil
open Floorplan.IL

/// Namespaces whose API can contain new primitives.
let watched =
    [ "System.Net"; "System.IO"; "System.Data"; "System.Diagnostics"; "Microsoft.AspNetCore.Http"; "Microsoft.AspNetCore.Routing"; "Microsoft.AspNetCore.Builder" ]

let isWatched (ns: string) =
    watched
    |> List.exists (fun w ->
        // System.Diagnostics itself (Process), but not System.Diagnostics.CodeAnalysis and other attribute namespaces.
        if w = "System.Diagnostics" then ns = w
        else ns = w || ns.StartsWith(w + "."))

type Member =
    { /// "Namespace.Type::Name(param,param)", stable across versions.
      Signature: string
      Type: string
      Method: MethodRef }

type Surface =
    { Members: Map<string, Member>
      /// Type full name to its base type and interfaces, for matching rules on derived types.
      Supertypes: Map<string, string list> }

let private visible (m: MethodDefinition) = m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly

let rec private publicTypes (t: TypeDefinition) =
    seq {
        if t.IsPublic || t.IsNestedPublic || t.IsNestedFamily then
            yield t

            for n in t.NestedTypes do
                yield! publicTypes n
    }

let read (directories: string list) : Surface =
    let types =
        directories
        |> List.collect (fun d -> Directory.GetFiles(d, "*.dll") |> Array.toList)
        |> List.collect (fun file ->
            try
                use md = ModuleDefinition.ReadModule(file, ReaderParameters(ReadingMode = ReadingMode.Immediate, InMemory = true))
                let asm = md.Assembly.Name.Name

                md.Types
                |> Seq.collect publicTypes
                |> Seq.map (fun t ->
                    let bases =
                        [ if not (isNull t.BaseType) then yield (CecilReader.typeRef asm t.BaseType).FullName
                          for i in t.Interfaces -> (CecilReader.typeRef asm i.InterfaceType).FullName ]

                    let ns = (CecilReader.typeRef asm t).Namespace

                    let members =
                        if isWatched ns then
                            t.Methods
                            |> Seq.filter visible
                            |> Seq.map (fun m ->
                                let r = CecilReader.methodRef asm m
                                let signature = r.LocalKey.Replace("`0(", "(")
                                { Signature = signature; Type = r.Type.FullName; Method = r })
                            |> Seq.toList
                        else
                            []

                    t.FullName, bases, members)
                |> Seq.toList
            with _ ->
                [])

    { Members = types |> List.collect (fun (_, _, ms) -> ms) |> List.map (fun m -> m.Signature, m) |> Map.ofList
      Supertypes = types |> List.map (fun (t, bs, _) -> t, bs) |> Map.ofList }

/// Every type the given type is assignable to, within the surface.
let supertypes (surface: Surface) (t: TypeRef) : Set<string> =
    let rec walk (seen: Set<string>) (name: string) =
        if seen.Contains name then
            seen
        else
            match surface.Supertypes.TryFind name with
            | Some bases -> bases |> List.fold walk (seen.Add name)
            | None -> seen.Add name

    walk Set.empty t.FullName
