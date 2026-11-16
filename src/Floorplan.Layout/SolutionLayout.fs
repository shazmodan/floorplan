/// The two-pass layout of plan section 7.2: units inside each project's box first, then the boxes. Units with no
/// dependency in either direction are left out of the graph and set out below it, one box per project.
module Floorplan.Layout.SolutionLayout

open Floorplan.Model

let nodeHeight = 32.0
let private charWidth = 7.2
let private boxPadding = 16.0
let private boxHeader = 28.0

/// Room above the unconnected units for the section's heading.
let detachedHeading = 40.0
let private detachedGap = 56.0
let private detachedNodeGap = 12.0
let private detachedBoxGap = 24.0
let private minimumDetachedWidth = 960.0

/// Width that fits a label in the site's default font, roughly.
let labelWidth (label: string) =
    max 72.0 (float label.Length * charWidth + 24.0)

let private offset (dx: float) (dy: float) (n: NodeLayout) = { n with X = n.X + dx; Y = n.Y + dy }

let private offsetPoint dx dy (p: Point) = { X = p.X + dx; Y = p.Y + dy }

let private offsetEdge dx dy (e: EdgeLayout) =
    { e with Points = e.Points |> List.map (offsetPoint dx dy) }

let private node id x y w h =
    { Id = id
      X = x
      Y = y
      Width = w
      Height = h
      Layer = 0
      Members = [] }

/// Rows of items of the given widths, none wider than the limit unless one item alone is: the index of each
/// item's row and its x within the row.
let private rows (limit: float) (gap: float) (widths: float list) =
    let mutable row = 0
    let mutable x = 0.0

    [ for w in widths do
          if x > 0.0 && x + w > limit then
              row <- row + 1
              x <- 0.0

          yield row, x
          x <- x + w + gap ]

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

    // Units that use or are used by another unit. The others (often reached only at run time, by reflection or
    // a framework) would all sit in the top layer and make it as wide as their number.
    let linked =
        dependencies
        |> List.filter (fun d -> d.FromUnit <> d.ToUnit && unitProject.ContainsKey d.FromUnit && unitProject.ContainsKey d.ToUnit)
        |> List.collect (fun d -> [ d.FromUnit; d.ToUnit ])
        |> set

    let connected, detached = units |> List.partition (fun u -> linked.Contains u.Id)

    let inGraph =
        projects |> List.filter (fun p -> connected |> List.exists (fun u -> u.ProjectId = p.Id))

    let inGraphIds = inGraph |> List.map (fun p -> p.Id) |> set

    // Pass 1: each project's connected units, laid out on their own.
    let inner =
        inGraph
        |> List.map (fun p ->
            let mine = connected |> List.filter (fun u -> u.ProjectId = p.Id)
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
    let crossPairs =
        dependencies
        |> List.choose (fun d ->
            match unitProject.TryFind d.FromUnit, unitProject.TryFind d.ToUnit with
            | Some a, Some b when a <> b && inGraphIds.Contains a && inGraphIds.Contains b -> Some(a, b)
            | _ -> None)
        |> List.distinct

    let boxEdges =
        [ for p in inGraph do
              for r in p.ProjectReferences do
                  if inGraphIds.Contains r then
                      yield { Layered.Edge.From = p.Id; Layered.Edge.To = r }
          for (a, b) in crossPairs do
              yield { Layered.Edge.From = a; Layered.Edge.To = b } ]
        |> List.distinct

    let boxes =
        Layered.layout
            { Layered.defaults with Layered.LayerGap = 72.0; Layered.NodeGap = 40.0 }
            (inGraph
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

    // The unconnected units, below the graph: a box per project, its units in rows, the boxes in rows too.
    let detachedWidth = max boxes.Width minimumDetachedWidth
    let detachedTop = (if inGraph.IsEmpty then 0.0 else boxes.Height + detachedGap) + detachedHeading

    let detachedGroups =
        projects
        |> List.choose (fun p ->
            match detached |> List.filter (fun u -> u.ProjectId = p.Id) |> List.sortBy (fun u -> u.DisplayName, u.Id) with
            | [] -> None
            | mine ->
                let inside = detachedWidth - 2.0 * boxPadding
                let widths = mine |> List.map (fun u -> labelWidth u.DisplayName)
                let placedInRows = rows inside detachedNodeGap widths
                let rowCount = (placedInRows |> List.map fst |> List.max) + 1

                let contentWidth =
                    List.zip placedInRows widths |> List.map (fun ((_, x), w) -> x + w) |> List.max

                let w = max contentWidth (labelWidth p.Name + 24.0) + 2.0 * boxPadding
                let h = boxHeader + 2.0 * boxPadding + float rowCount * nodeHeight + float (rowCount - 1) * detachedNodeGap
                Some(p.Id, mine, List.zip placedInRows widths, w, h))

    let groupRows = rows detachedWidth detachedBoxGap (detachedGroups |> List.map (fun (_, _, _, w, _) -> w))

    let rowTops =
        let heights =
            List.zip groupRows detachedGroups
            |> List.groupBy (fun ((row, _), _) -> row)
            |> List.map (fun (row, gs) -> row, gs |> List.map (fun (_, (_, _, _, _, h)) -> h) |> List.max)
            |> List.sortBy fst

        heights
        |> List.scan (fun top (_, h) -> top + h + detachedBoxGap) detachedTop
        |> List.truncate heights.Length
        |> List.zip (heights |> List.map fst)
        |> Map.ofList

    let detachedBoxes, detachedNodes =
        List.zip groupRows detachedGroups
        |> List.map (fun ((row, x), (projectId, mine, cells, w, h)) ->
            let y = rowTops[row]
            let box = node projectId x y w h

            let nodes =
                List.zip mine cells
                |> List.map (fun (u, ((r, cx), cw)) ->
                    node u.Id (x + boxPadding + cx) (y + boxHeader + boxPadding + float r * (nodeHeight + detachedNodeGap)) cw nodeHeight)

            box, nodes)
        |> List.unzip

    let detachedNodes = List.concat detachedNodes

    let totalWidth =
        List.max (boxes.Width :: (detachedBoxes |> List.map (fun b -> b.X + b.Width)))

    let totalHeight =
        List.max (boxes.Height :: (detachedBoxes |> List.map (fun b -> b.Y + b.Height)))

    // The graph is centred over the unconnected units when they are wider.
    let shift = (totalWidth - boxes.Width) / 2.0

    let projectBoxes = inGraph |> List.map (fun p -> boxFor p.Id |> offset shift 0.0)
    let shiftedBox = projectBoxes |> List.map (fun b -> b.Id, b) |> Map.ofList

    let unitNodes, innerEdges =
        inGraph
        |> List.map (fun p ->
            let box = shiftedBox[p.Id]
            let g = inner[p.Id]
            let dx = box.X + boxPadding + (box.Width - 2.0 * boxPadding - g.Width) / 2.0
            let dy = box.Y + boxHeader + boxPadding

            g.Nodes |> List.map (offset dx dy), g.Edges |> List.map (offsetEdge dx dy))
        |> List.unzip

    let projectEdges =
        let wanted = set crossPairs

        boxes.Edges
        |> List.filter (fun e -> wanted.Contains(e.From, e.To))
        |> List.map (offsetEdge shift 0.0)

    { ProjectGraph = projectGraph
      UnitGraph =
        { Width = totalWidth
          Height = totalHeight
          Nodes = List.concat unitNodes @ detachedNodes
          Edges = List.concat innerEdges }
      ProjectBoxes = projectBoxes
      ProjectEdges = projectEdges
      DetachedBoxes = detachedBoxes }
