/// Joins IL to units by source location (plan section 3.1): a PDB sequence point gives the file and line of
/// any instruction, including those in compiler-generated closures, and the unit is the innermost one whose
/// range contains that line. Nothing here depends on a language's naming conventions; the type-name fallback
/// is used only for code without symbols.
module Floorplan.Analysis.Attribution

open System
open System.IO
open Floorplan.Model
open Floorplan.IL
open Floorplan.Structure

let toLocation (p: SourcePoint) : Location =
    { File = p.File
      StartLine = p.StartLine
      StartColumn = p.StartColumn
      EndLine = p.EndLine
      EndColumn = p.EndColumn }

type Attributor(units: CodeUnit list, projects: AnalyzedProject list) =
    let normalize (path: string) =
        try Path.GetFullPath(path).ToLowerInvariant() with _ -> path.ToLowerInvariant()

    let byFile =
        units
        |> List.choose (fun u -> u.Location |> Option.map (fun l -> normalize l.File, u))
        |> List.groupBy fst
        |> List.map (fun (f, us) -> f, us |> List.map snd)
        |> dict

    let byProject = units |> List.groupBy (fun u -> u.ProjectId) |> dict
    let limited = projects |> List.filter (fun p -> p.SupportLevel = SupportLevel.Limited) |> List.map (fun p -> p.ProjectId) |> set

    let contains (l: Location) (line: int) (column: int) =
        (line > l.StartLine || (line = l.StartLine && column >= l.StartColumn))
        && (line < l.EndLine || (line = l.EndLine && column <= l.EndColumn))

    /// The unit for a source point.
    member _.ByPoint(projectId: string, point: SourcePoint) : CodeUnit option =
        match byFile.TryGetValue(normalize point.File) with
        | true, candidates ->
            candidates
            |> List.filter (fun u -> u.ProjectId = projectId && contains u.Location.Value point.StartLine point.StartColumn)
            |> List.sortBy (fun u -> u.Name.Length)
            |> List.tryLast
        | _ -> None

    /// The unit by the declaring type's name: the longest unit name the type name starts with. A unit with an
    /// empty name is the project's root namespace and contains every type.
    member _.ByTypeName(projectId: string, t: TypeRef) : CodeUnit option =
        match byProject.TryGetValue projectId with
        | true, candidates ->
            let name = t.FullName.Replace('/', '.')
            let outer = t.FullName.Split('/').[0]

            // A type known by an alias (a Razor page's) is in the unit that lists it.
            match candidates |> List.tryFind (fun u -> u.Aliases |> List.exists (fun a -> a.Type = outer)) with
            | Some u -> Some u
            | None ->

            candidates
            |> List.filter (fun u -> u.Name = "" || name = u.Name || name.StartsWith(u.Name + ".") || name.StartsWith(u.Name + "@"))
            |> List.sortBy (fun u -> u.Name.Length)
            |> List.tryLast
        | _ -> None

    /// The unit a call site belongs to: the project unit for limited-support projects, else by source point,
    /// else by type name.
    member this.Attribute(projectId: string, caller: MethodRef, point: SourcePoint option) : CodeUnit option =
        if limited.Contains projectId then
            units |> List.tryFind (fun u -> u.Id = UnitId.forProject projectId)
        else
            match point |> Option.bind (fun p -> this.ByPoint(projectId, p)) with
            | Some u -> Some u
            | None -> this.ByTypeName(projectId, caller.Type)
