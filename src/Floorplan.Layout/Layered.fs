/// A layered (Sugiyama-style) layout: collapse cycles, rank by longest path, order by barycenter, then place.
/// It never assumes the graph is acyclic (plan section 3.5).
module Floorplan.Layout.Layered

open System.Collections.Generic
open Floorplan.Model

type Node =
    { Id: string
      Width: float
      Height: float }

type Edge = { From: string; To: string }

let node (id: string) (width: float) (height: float) : Node = { Id = id; Width = width; Height = height }

let edge (from: string) (target: string) : Edge = { From = from; To = target }

type Settings =
    { LayerGap: float
      NodeGap: float
      /// Barycenter sweeps; more gives fewer crossings on large graphs.
      Sweeps: int }

let defaults =
    { LayerGap = 64.0
      NodeGap = 24.0
      Sweeps = 8 }

/// Strongly connected components (Tarjan), each as a list of node ids, in a deterministic order.
let stronglyConnected (nodes: string list) (edges: Edge list) : string list list =
    let successors =
        edges
        |> List.groupBy (fun e -> e.From)
        |> List.map (fun (k, es) -> k, es |> List.map (fun e -> e.To) |> List.distinct |> List.sort)
        |> dict

    let index = Dictionary<string, int>()
    let low = Dictionary<string, int>()
    let onStack = HashSet<string>()
    let stack = Stack<string>()
    let result = ResizeArray<string list>()
    let mutable counter = 0

    let rec connect (v: string) =
        index[v] <- counter
        low[v] <- counter
        counter <- counter + 1
        stack.Push v
        onStack.Add v |> ignore

        match successors.TryGetValue v with
        | true, ws ->
            for w in ws do
                if not (index.ContainsKey w) then
                    connect w
                    low[v] <- min low[v] low[w]
                elif onStack.Contains w then
                    low[v] <- min low[v] index[w]
        | _ -> ()

        if low[v] = index[v] then
            let comp = ResizeArray()
            let mutable go = true

            while go do
                let w = stack.Pop()
                onStack.Remove w |> ignore
                comp.Add w
                go <- w <> v

            result.Add(comp |> Seq.sort |> Seq.toList)

    let known = HashSet(nodes)

    for n in nodes |> List.sort do
        if not (index.ContainsKey n) then
            connect n

    result
    |> Seq.map (List.filter known.Contains)
    |> Seq.filter (fun c -> not c.IsEmpty)
    |> Seq.toList

/// Lays out the graph. Edges point downwards: a node sits above everything it depends on.
let layout (settings: Settings) (nodes: Node list) (edges: Edge list) : GraphLayout =
    if nodes.IsEmpty then
        AnalysisModel.emptyGraph
    else
        let nodeIds = nodes |> List.map (fun n -> n.Id) |> set
        let edges = edges |> List.filter (fun e -> nodeIds.Contains e.From && nodeIds.Contains e.To && e.From <> e.To)

        // 1. Collapse cycles. A group is named after its first member.
        let groups = stronglyConnected (nodes |> List.map (fun n -> n.Id)) edges
        let groupOf = Dictionary<string, string>()

        for g in groups do
            for m in g do
                groupOf[m] <- g.Head

        let members = groups |> List.map (fun g -> g.Head, g) |> dict
        let byId = nodes |> List.map (fun n -> n.Id, n) |> dict

        let size (g: string) =
            let ms = members[g] |> List.map (fun m -> byId[m])

            if ms.Length = 1 then
                ms.Head.Width, ms.Head.Height
            else
                // A collapsed group is drawn as one wide node.
                (ms |> List.sumBy (fun m -> m.Width)) + settings.NodeGap * float (ms.Length - 1),
                ms |> List.map (fun m -> m.Height) |> List.max

        let groupEdges =
            edges
            |> List.map (fun e -> groupOf[e.From], groupOf[e.To])
            |> List.filter (fun (a, b) -> a <> b)
            |> List.distinct

        let groupIds = groups |> List.map (fun g -> g.Head)

        let preds =
            groupEdges |> List.groupBy snd |> List.map (fun (k, es) -> k, es |> List.map fst) |> dict

        let succs =
            groupEdges |> List.groupBy fst |> List.map (fun (k, es) -> k, es |> List.map snd) |> dict

        let get (d: IDictionary<string, string list>) k =
            match d.TryGetValue k with
            | true, v -> v
            | _ -> []

        // 2. Rank by longest path from the top (nodes nothing depends on).
        let rank = Dictionary<string, int>()

        let rec rankOf (g: string) =
            match rank.TryGetValue g with
            | true, r -> r
            | _ ->
                let r =
                    match get preds g with
                    | [] -> 0
                    | ps -> 1 + (ps |> List.map rankOf |> List.max)

                rank[g] <- r
                r

        for g in groupIds do
            rankOf g |> ignore

        let layerCount = (rank.Values |> Seq.max) + 1

        let layers =
            Array.init layerCount (fun l -> groupIds |> List.filter (fun g -> rank[g] = l) |> List.sort |> ResizeArray)

        // 3. Order within layers by barycenter, sweeping down then up.
        let position () =
            let p = Dictionary<string, float>()

            for layer in layers do
                layer |> Seq.iteri (fun i g -> p[g] <- float i)

            p

        let reorder (layer: ResizeArray<string>) (neighbours: string -> string list) =
            let pos = position ()

            let keyed =
                layer
                |> Seq.mapi (fun i g ->
                    let ns = neighbours g |> List.filter pos.ContainsKey

                    let bary =
                        if ns.IsEmpty then float i else ns |> List.averageBy (fun n -> pos[n])

                    g, bary, i)
                |> Seq.sortBy (fun (_, b, i) -> b, i)
                |> Seq.map (fun (g, _, _) -> g)
                |> Seq.toList

            layer.Clear()
            layer.AddRange keyed

        for _ in 1 .. settings.Sweeps do
            for l in 1 .. layerCount - 1 do
                reorder layers[l] (get preds)

            for l in layerCount - 2 .. -1 .. 0 do
                reorder layers[l] (get succs)

        // 4. Place. Layers are centered horizontally; each layer is as tall as its tallest node.
        let layerWidth (layer: ResizeArray<string>) =
            (layer |> Seq.sumBy (fun g -> fst (size g)))
            + settings.NodeGap * float (max 0 (layer.Count - 1))

        let width = layers |> Array.map layerWidth |> Array.max

        let layerHeights =
            layers |> Array.map (fun l -> l |> Seq.map (size >> snd) |> Seq.fold max 0.0)

        let placed = Dictionary<string, NodeLayout>()
        let mutable y = 0.0

        for l in 0 .. layerCount - 1 do
            let mutable x = (width - layerWidth layers[l]) / 2.0

            for g in layers[l] do
                let w, h = size g

                placed[g] <-
                    { Id = g
                      X = x
                      Y = y + (layerHeights[l] - h) / 2.0
                      Width = w
                      Height = h
                      Layer = l
                      Members = if members[g].Length > 1 then members[g] else [] }

                x <- x + w + settings.NodeGap

            y <- y + layerHeights[l] + settings.LayerGap

        let height = y - settings.LayerGap

        // 5. Route edges between the original nodes, from the bottom of the source to the top of the target.
        let routed =
            edges
            |> List.map (fun e -> e, groupOf[e.From], groupOf[e.To])
            |> List.filter (fun (_, a, b) -> a <> b)
            |> List.map (fun (e, a, b) ->
                let s = placed[a]
                let t = placed[b]

                { From = e.From
                  To = e.To
                  Points =
                    [ { X = s.X + s.Width / 2.0; Y = s.Y + s.Height }
                      { X = t.X + t.Width / 2.0; Y = t.Y } ] })

        { Width = width
          Height = height
          Nodes = groupIds |> List.map (fun g -> placed[g]) |> List.sortBy (fun n -> n.Layer, n.X)
          Edges = routed }
