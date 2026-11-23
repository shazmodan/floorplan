/// All assemblies one analysis can see: user assemblies, their dependencies and the shared frameworks.
/// Dependencies and framework assemblies are read on first use.
namespace Floorplan.IL

open System
open System.Collections.Generic
open System.IO
open System.Reflection

module Origin =
    let private frameworkMarkers =
        [ "Microsoft.NETCore.App"
          "Microsoft.AspNetCore.App"
          "Microsoft.WindowsDesktop.App"
          "NETStandard.Library.Ref" ]

    /// An assembly file's version and then its file version (AWS keeps the assembly version at 3.3.0.0 across
    /// releases), each 0.0 when it cannot be read.
    let versionOf (path: string) =
        let assembly =
            try
                AssemblyName.GetAssemblyName(path).Version
            with _ ->
                Version(0, 0)

        let file =
            try
                match Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion with
                | null -> Version(0, 0)
                | v ->
                    match Version.TryParse(v.Split(' ').[0]) with
                    | true, parsed -> parsed
                    | _ -> Version(0, 0)
            with _ ->
                Version(0, 0)

        assembly, file

    /// A shared-framework assembly, judged by where it lives: a reference pack or the shared runtime folder.
    let isFrameworkPath (path: string) =
        let p = path.Replace('\\', '/')

        frameworkMarkers
        |> List.exists (fun m -> p.Contains("/packs/" + m) || p.Contains("/shared/" + m + "/") || p.Contains("/" + m.ToLowerInvariant() + ".ref/"))

type AssemblySet(reader: IAssemblyReader, userAssemblies: (string * string) list, searchPaths: string list) =
    // Every candidate file, by simple assembly name. The first path given wins, except that a package's copy wins
    // over a shared framework's: an assembly that is part of ASP.NET Core and that a project's build resolved to a
    // newer package (OpenTelemetry brings Microsoft.Extensions.DependencyInjection.Abstractions 9) runs as the
    // package's, and its code is there to follow for that project. Whether it is followed is the project's: for
    // a project whose build resolved the framework's copy it is still framework code (`AlsoInFramework`). Of two
    // packages' copies (projects on different versions of Microsoft.Extensions.Hosting), the higher version wins: it
    // keeps the members the older one has, so every project's calls resolve, whatever the order of the projects.
    let candidates = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    // Names a shared framework also has a copy of.
    let inFramework = HashSet<string>(StringComparer.OrdinalIgnoreCase)

    do
        let users = HashSet<string>(StringComparer.OrdinalIgnoreCase)

        for (_, path) in userAssemblies do
            let name = Path.GetFileNameWithoutExtension path
            candidates[name] <- path
            users.Add name |> ignore

        for path in searchPaths do
            let name = Path.GetFileNameWithoutExtension path

            if Origin.isFrameworkPath path then
                inFramework.Add name |> ignore

            match candidates.TryGetValue name with
            | false, _ when File.Exists path -> candidates[name] <- path
            | true, existing when not (users.Contains name)
                                  && Origin.isFrameworkPath existing
                                  && not (Origin.isFrameworkPath path)
                                  && File.Exists path ->
                candidates[name] <- path
            | true, existing when not (users.Contains name)
                                  && existing <> path
                                  && not (Origin.isFrameworkPath existing)
                                  && not (Origin.isFrameworkPath path)
                                  && File.Exists path
                                  && Origin.versionOf path > Origin.versionOf existing ->
                candidates[name] <- path
            | _ -> ()

    let userByName =
        userAssemblies
        |> List.map (fun (projectId, path) -> Path.GetFileNameWithoutExtension path, projectId)
        |> dict

    let loaded = Dictionary<string, AssemblyDef option>(StringComparer.OrdinalIgnoreCase)
    // Types by (assembly, full name); two projects may define types with the same full name.
    let types = Dictionary<struct (string * string), TypeDef * AssemblyDef>()
    // Fallback by full name alone, for references through facades.
    let typesByName = Dictionary<string, TypeDef * AssemblyDef>()
    let methods = Dictionary<string, MethodDef * TypeDef>()
    let supertypeCache = Dictionary<TypeRef, Set<string>>()

    let key (assembly: string) (fullName: string) = struct (assembly.ToLowerInvariant(), fullName)

    let index (a: AssemblyDef) =
        for t in a.Types do
            types.TryAdd(key a.Name t.Ref.FullName, (t, a)) |> ignore
            typesByName.TryAdd(t.Ref.FullName, (t, a)) |> ignore

            for m in t.Methods do
                methods.TryAdd(m.Ref.Key, (m, t)) |> ignore

    let load (name: string) =
        match loaded.TryGetValue name with
        | true, a -> a
        | _ ->
            let result =
                match candidates.TryGetValue name with
                | true, path ->
                    try
                        let a = reader.Read(path, userByName.ContainsKey name)
                        index a
                        Some a
                    with _ ->
                        None
                | _ -> None

            loaded[name] <- result
            result

    /// Simple names of all assemblies that could be loaded.
    member _.KnownAssemblies = candidates.Keys |> Seq.toList

    member _.TryLoad(name: string) = load name

    member _.PathOf(name: string) =
        match candidates.TryGetValue name with
        | true, p -> Some p
        | _ -> None

    member this.Origin(assemblyName: string) : AssemblyOrigin =
        match userByName.TryGetValue assemblyName with
        | true, projectId -> User projectId
        | _ ->
            match this.PathOf assemblyName with
            | Some p when Origin.isFrameworkPath p -> Framework
            | Some _ -> Dependency
            // Unknown assemblies are treated as framework: never analyzed, never summarized.
            | None -> Framework

    /// Whether a shared framework also has a copy of the assembly, whichever copy was read.
    member _.AlsoInFramework(assemblyName: string) = inFramework.Contains assemblyName

    member this.IsUser(assemblyName: string) =
        match this.Origin assemblyName with
        | User _ -> true
        | _ -> false

    member _.UserAssemblies =
        userAssemblies |> List.choose (fun (_, path) -> load (Path.GetFileNameWithoutExtension path))

    /// Resolves a type, loading its assembly and following type forwarders.
    member this.TryType(t: TypeRef) : (TypeDef * AssemblyDef) option =
        match types.TryGetValue(key t.Assembly t.FullName) with
        | true, found -> Some found
        | _ ->
            let rec find (assembly: string) (hops: int) =
                if hops > 4 then
                    None
                else
                    match load assembly with
                    | None -> None
                    | Some a ->
                        match types.TryGetValue(key a.Name t.FullName) with
                        | true, found -> Some found
                        | _ ->
                            // Nested types are forwarded under their outermost type.
                            let outer = t.FullName.Split('/').[0]

                            match a.Forwarders.TryFind outer with
                            | Some target -> find target (hops + 1)
                            | None -> None

            match find t.Assembly 0 with
            | Some found -> Some found
            | None ->
                match typesByName.TryGetValue t.FullName with
                | true, found -> Some found
                | _ -> None

    /// The definition behind a method reference, if its assembly can be read.
    member this.TryMethod(m: MethodRef) : (MethodDef * TypeDef) option =
        match methods.TryGetValue m.Key with
        | true, found -> Some found
        | _ ->
            // The type may live in another assembly than the reference names (forwarders, facades).
            match this.TryType m.Type with
            | Some(def, _) ->
                def.Methods
                |> List.tryFind (fun d -> d.Ref.LocalKey = m.LocalKey)
                |> Option.map (fun d -> d, def)
            | None -> None

    /// The type itself, then its base types, nearest first.
    member this.BaseChain(t: TypeRef) : TypeRef list =
        let rec walk (t: TypeRef) (seen: Set<string>) =
            if seen.Contains t.FullName then
                []
            else
                match this.TryType t with
                | Some(def, _) ->
                    t
                    :: (match def.BaseType with
                        | Some b -> walk b (seen.Add t.FullName)
                        | None -> [])
                | None -> [ t ]

        walk t Set.empty

    /// Every type the given type is assignable to: itself, its base types and all their interfaces.
    member this.Supertypes(t: TypeRef) : Set<string> =
        match supertypeCache.TryGetValue t with
        | true, s -> s
        | _ ->
            let s = this.ComputeSupertypes t
            supertypeCache[t] <- s
            s

    member private this.ComputeSupertypes(t: TypeRef) : Set<string> =
        let result = HashSet<string>()

        let rec addInterface (i: TypeRef) =
            if result.Add i.FullName then
                match this.TryType i with
                | Some(def, _) -> def.Interfaces |> List.iter addInterface
                | None -> ()

        for b in this.BaseChain t do
            result.Add b.FullName |> ignore

            match this.TryType b with
            | Some(def, _) -> def.Interfaces |> List.iter addInterface
            | None -> ()

        set result

    member this.IsSubtypeOf(t: TypeRef, ofType: string) = (this.Supertypes t).Contains ofType

    /// The method that runs when `virtualMethod` is called on an object of exact type `t`, if `t` defines or
    /// inherits an implementation of it. Matching is by name and parameter count, plus explicit overrides.
    member this.Implementation(t: TypeRef, virtualMethod: MethodRef) : MethodRef option =
        let chain = this.BaseChain t

        chain
        |> List.tryPick (fun c ->
            match this.TryType c with
            | None -> None
            | Some(def, _) ->
                let explicitOverride =
                    def.Methods
                    |> List.tryFind (fun m ->
                        m.Overrides
                        |> List.exists (fun o -> o.LocalKey = virtualMethod.LocalKey || (o.Name = virtualMethod.Name && o.Type.FullName = virtualMethod.Type.FullName)))

                match explicitOverride with
                | Some m when not m.IsAbstract -> Some m.Ref
                | _ ->
                    def.Methods
                    |> List.tryFind (fun m ->
                        m.IsVirtual
                        && not m.IsAbstract
                        && m.Ref.Name = virtualMethod.Name
                        && m.Ref.ParameterCount = virtualMethod.ParameterCount)
                    |> Option.map (fun m -> m.Ref))

    interface IDisposable with
        member _.Dispose() = reader.Dispose()
