/// The two-pass layout of plan section 7.2: units inside each project's box first, then the boxes.
module Floorplan.Layout.SolutionLayout

open Floorplan.Model

let nodeHeight = 32.0
let private charWidth = 7.2
let private boxPadding = 16.0
let private boxHeader = 28.0

/// Width that fits a label in the site's default font, roughly.
let labelWidth (label: string) =
    max 72.0 (float label.Length * charWidth + 24.0)

let private offset (dx: float) (dy: float) (n: NodeLayout) = { n with X = n.X + dx; Y = n.Y + dy }

let private offsetPoint dx dy (p: Point) = { X = p.X + dx; Y = p.Y + dy }

let compute (projects: Project list) (units: CodeUnit list) (dependencies: Dependency list) : Layout =
    let projectIds = projects |> List.map (fun p -> p.Id) |> set

    // Projects and their references.
    let projectGraph =
        Layered.layout
            Layered.defaults
            (projects
             |> List.map (fun p ->
                 { Layered.Node.Id = p.Id
                   Width = labelWidth p.Name
                   Height = nodeHeight + 8.0 }))
            (projects
             |> List.collect (fun p ->
                 p.ProjectReferences
                 |> List.filter projectIds.Contains
                 |> List.map (fun r -> { Layered.Edge.From = p.Id; Layered.Edge.To = r })))

    let unitProject = units |> List.map (fun u -> u.Id, u.ProjectId) |> Map.ofList

    // Pass 1: each project's units, laid out on their own.
    let inner =
        projects
        |> List.map (fun p ->
            let mine = units |> List.filter (fun u -> u.ProjectId = p.Id)
            let ids = mine |> List.map (fun u -> u.Id) |> set

            let graph =
                Layered.layout
                    { Layered.defaults with LayerGap = 48.0 }
                    (mine
                     |> List.map (fun u ->
                         { Layered.Node.Id = u.Id
                           Width = labelWidth u.DisplayName
                           Height = nodeHeight }))
                    (dependencies
                     |> List.filter (fun d -> ids.Contains d.FromUnit && ids.Contains d.ToUnit)
                     |> List.map (fun d -> { Layered.Edge.From = d.FromUnit; Layered.Edge.To = d.ToUnit }))

            p.Id, graph)
        |> Map.ofList

    let boxSize (projectId: string) =
        let g = inner[projectId]
        let p = projects |> List.find (fun p -> p.Id = projectId)
        // Room for the name and the collapse toggle in the header.
        let contentWidth = max g.Width (labelWidth p.Name + 24.0)
        contentWidth + 2.0 * boxPadding, g.Height + boxHeader + 2.0 * boxPadding

    // Pass 2: the boxes, connected by project references and by cross-project unit dependencies.
    let boxEdges =
        [ for p in projects do
              for r in p.ProjectReferences do
                  if projectIds.Contains r then
                      yield { Layered.Edge.From = p.Id; Layered.Edge.To = r }
          for d in dependencies do
              match unitProject.TryFind d.FromUnit, unitProject.TryFind d.ToUnit with
              | Some a, Some b when a <> b -> yield { Layered.Edge.From = a; Layered.Edge.To = b }
              | _ -> () ]
        |> List.distinct

    let boxes =
        Layered.layout
            { Layered.defaults with Layered.LayerGap = 72.0; Layered.NodeGap = 40.0 }
            (projects
             |> List.map (fun p ->
                 let w, h = boxSize p.Id
                 { Layered.Node.Id = p.Id; Layered.Node.Width = w; Layered.Node.Height = h }))
            boxEdges

    let boxOf = boxes.Nodes |> List.map (fun b -> b.Id, b) |> Map.ofList

    // Boxes of projects caught in a reference cycle share one node; give each member its own slice of it.
    let boxFor (projectId: string) =
        match boxOf.TryFind projectId with
        | Some b -> b
        | None ->
            let group = boxes.Nodes |> List.find (fun b -> b.Members |> List.contains projectId)
            let before = group.Members |> List.takeWhile (fun m -> m <> projectId)
            let x = before |> List.sumBy (fun m -> fst (boxSize m) + 40.0)
            let w, h = boxSize projectId

            { group with
                Id = projectId
                X = group.X + x
                Width = w
                Height = h
                Members = [] }

    let projectBoxes = projects |> List.map (fun p -> boxFor p.Id)

    let unitNodes, innerEdges =
        projects
        |> List.map (fun p ->
            let box = boxFor p.Id
            let g = inner[p.Id]
            let dx = box.X + boxPadding + (box.Width - 2.0 * boxPadding - g.Width) / 2.0
            let dy = box.Y + boxHeader + boxPadding

            g.Nodes |> List.map (offset dx dy),
            g.Edges |> List.map (fun e -> { e with Points = e.Points |> List.map (offsetPoint dx dy) }))
        |> List.unzip

    let unitNodes = List.concat unitNodes

    let nodeOf =
        unitNodes
        |> List.collect (fun n -> (n.Id, n) :: (n.Members |> List.map (fun m -> m, n)))
        |> Map.ofList

    let crossEdges =
        dependencies
        |> List.filter (fun d ->
            match unitProject.TryFind d.FromUnit, unitProject.TryFind d.ToUnit with
            | Some a, Some b -> a <> b
            | _ -> false)
        |> List.choose (fun d ->
            match nodeOf.TryFind d.FromUnit, nodeOf.TryFind d.ToUnit with
            | Some s, Some t ->
                let start, finish =
                    if t.Y > s.Y then
                        { X = s.X + s.Width / 2.0; Y = s.Y + s.Height }, { X = t.X + t.Width / 2.0; Y = t.Y }
                    else
                        { X = s.X + s.Width / 2.0; Y = s.Y }, { X = t.X + t.Width / 2.0; Y = t.Y + t.Height }

                Some
                    { From = d.FromUnit
                      To = d.ToUnit
                      Points = [ start; finish ] }
            | _ -> None)

    { ProjectGraph = projectGraph
      UnitGraph =
        { Width = boxes.Width
          Height = boxes.Height
          Nodes = unitNodes
          Edges = List.concat innerEdges @ crossEdges }
      ProjectBoxes = projectBoxes }
