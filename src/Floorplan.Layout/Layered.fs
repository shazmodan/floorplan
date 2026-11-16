/// A layered (Sugiyama-style) layout: collapse cycles, rank by longest path, split long edges into virtual nodes,
/// order by barycenter, then place each node near its neighbours.
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

        // 3. An edge that spans several layers gets a chain of virtual nodes, one per layer it crosses: ordering
        //    then keeps it clear of the real nodes, and routing passes through the gaps it was given.
        let layerOf = Dictionary<string, int>(rank)
        let chains = Dictionary<string * string, string list>()
        // An ordinal test: culture-aware comparison ignores control characters, so every id would match.
        let isVirtual (id: string) = id.Length > 0 && id[0] = '\u0001'

        let layeredEdges =
            [ for (a, b) in groupEdges do
                  let ra, rb = rank[a], rank[b]

                  if rb - ra <= 1 then
                      yield a, b
                  else
                      let chain = [ for l in ra + 1 .. rb - 1 -> $"\u0001{a}\u0001{b}\u0001{l}" ]

                      for v, l in List.zip chain [ ra + 1 .. rb - 1 ] do
                          layerOf[v] <- l

                      chains[(a, b)] <- chain
                      yield! List.pairwise (a :: chain @ [ b ]) ]

        let preds =
            layeredEdges |> List.groupBy snd |> List.map (fun (k, es) -> k, es |> List.map fst) |> dict

        let succs =
            layeredEdges |> List.groupBy fst |> List.map (fun (k, es) -> k, es |> List.map snd) |> dict

        let virtualWidth = 6.0

        let sizeOf (id: string) =
            if isVirtual id then virtualWidth, 0.0 else size id

        let layers =
            Array.init layerCount (fun l -> layerOf.Keys |> Seq.filter (fun g -> layerOf[g] = l) |> Seq.sort |> ResizeArray)

        // 4. Order within layers by barycenter, sweeping down then up, and keep the order with the fewest crossings.
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

        let crossings () =
            let pos = position ()
            let mutable total = 0

            for l in 0 .. layerCount - 2 do
                let es =
                    layers[l]
                    |> Seq.collect (fun a -> get succs a |> List.map (fun b -> pos[a], pos[b]))
                    |> Seq.toArray

                for i in 0 .. es.Length - 1 do
                    for j in i + 1 .. es.Length - 1 do
                        let (a1, b1), (a2, b2) = es[i], es[j]

                        if (a1 - a2) * (b1 - b2) < 0.0 then
                            total <- total + 1

            total

        let snapshot () = layers |> Array.map List.ofSeq
        let mutable best = snapshot ()
        let mutable fewest = crossings ()

        for _ in 1 .. settings.Sweeps do
            for l in 1 .. layerCount - 1 do
                reorder layers[l] (get preds)

            for l in layerCount - 2 .. -1 .. 0 do
                reorder layers[l] (get succs)

            let c = crossings ()

            if c < fewest then
                fewest <- c
                best <- snapshot ()

        for l in 0 .. layerCount - 1 do
            layers[l].Clear()
            layers[l].AddRange best[l]

        // 5. Place. Each node sits as close as its order allows to the mean of its neighbours above (sweeping
        //    down) or below (sweeping up): the closest positions that keep the order and the gaps are found
        //    exactly, by pooling adjacent violators.
        let gapBetween (a: string) (b: string) =
            if isVirtual a || isVirtual b then settings.NodeGap / 2.0 else settings.NodeGap

        let centre = Dictionary<string, float>()

        for layer in layers do
            let mutable x = 0.0

            for g in layer do
                let w = fst (sizeOf g)
                centre[g] <- x + w / 2.0
                x <- x + w + settings.NodeGap

        let place (layer: ResizeArray<string>) (neighbours: string -> string list) =
            let n = layer.Count

            if n > 0 then
                let desired =
                    layer
                    |> Seq.map (fun g ->
                        match neighbours g with
                        | [] -> centre[g]
                        | ns -> ns |> List.averageBy (fun x -> centre[x]))
                    |> Seq.toArray

                // Offsets that keep neighbours apart: x[i] - x[i-1] >= offset[i] - offset[i-1].
                let offset = Array.zeroCreate n

                for i in 1 .. n - 1 do
                    let a, b = layer[i - 1], layer[i]
                    offset[i] <- offset[i - 1] + fst (sizeOf a) / 2.0 + gapBetween a b + fst (sizeOf b) / 2.0

                // Non-decreasing least-squares fit of desired - offset, by pooling adjacent violators.
                let blocks = Stack<float * float * int>()

                for i in 0 .. n - 1 do
                    let weight = if isVirtual layer[i] then 0.5 else 1.0
                    let mutable sum = (desired[i] - offset[i]) * weight
                    let mutable w = weight
                    let mutable count = 1
                    let mutable merging = true

                    while merging && blocks.Count > 0 do
                        let (s2, w2, c2) = blocks.Peek()

                        if s2 / w2 >= sum / w then
                            blocks.Pop() |> ignore
                            sum <- sum + s2
                            w <- w + w2
                            count <- count + c2
                        else
                            merging <- false

                    blocks.Push((sum, w, count))

                let fitted = Array.zeroCreate n
                let mutable i = n

                for (s, w, c) in blocks do
                    for _ in 1..c do
                        i <- i - 1
                        fitted[i] <- s / w

                for i in 0 .. n - 1 do
                    centre[layer[i]] <- fitted[i] + offset[i]

        for _ in 1..6 do
            for l in 1 .. layerCount - 1 do
                place layers[l] (get preds)

            for l in layerCount - 2 .. -1 .. 0 do
                place layers[l] (get succs)

        // Down once more, so that each layer settles under what uses it.
        for l in 1 .. layerCount - 1 do
            place layers[l] (get preds)

        let left =
            layerOf.Keys |> Seq.map (fun g -> centre[g] - fst (sizeOf g) / 2.0) |> Seq.min

        let width =
            (layerOf.Keys |> Seq.map (fun g -> centre[g] + fst (sizeOf g) / 2.0) |> Seq.max) - left

        let layerHeights =
            layers |> Array.map (fun l -> l |> Seq.map (sizeOf >> snd) |> Seq.fold max 0.0)

        let layerTops = Array.zeroCreate layerCount
        let mutable y = 0.0

        for l in 0 .. layerCount - 1 do
            layerTops[l] <- y
            y <- y + layerHeights[l] + settings.LayerGap

        let height = y - settings.LayerGap
        let cx (g: string) = centre[g] - left

        let placed = Dictionary<string, NodeLayout>()

        for g in groupIds do
            let l = layerOf[g]
            let w, h = size g

            placed[g] <-
                { Id = g
                  X = cx g - w / 2.0
                  Y = layerTops[l] + (layerHeights[l] - h) / 2.0
                  Width = w
                  Height = h
                  Layer = l
                  Members = if members[g].Length > 1 then members[g] else [] }

        // 6. Route edges between the original nodes, from the bottom of the source to the top of the target,
        //    through the virtual nodes of a long edge. The edges leaving (entering) a node are spread along its
        //    bottom (top) in the order of where they go next, so that they do not cross where they meet it.
        let firstHop (a: string, b: string) =
            match chains.TryGetValue((a, b)) with
            | true, c -> c.Head
            | _ -> b

        let lastHop (a: string, b: string) =
            match chains.TryGetValue((a, b)) with
            | true, c -> List.last c
            | _ -> a

        let spread (count: int) (index: int) (node: NodeLayout) =
            if count <= 1 then
                node.X + node.Width / 2.0
            else
                node.X + node.Width * 0.2 + node.Width * 0.6 * float index / float (count - 1)

        let leaving =
            groupEdges |> List.groupBy fst |> List.map (fun (a, es) -> a, es |> List.sortBy (fun e -> centre[firstHop e], snd e)) |> dict

        let entering =
            groupEdges |> List.groupBy snd |> List.map (fun (b, es) -> b, es |> List.sortBy (fun e -> centre[lastHop e], fst e)) |> dict

        let routed =
            edges
            |> List.map (fun e -> e, groupOf[e.From], groupOf[e.To])
            |> List.filter (fun (_, a, b) -> a <> b)
            |> List.map (fun (e, a, b) ->
                let s = placed[a]
                let t = placed[b]
                let key = a, b
                let outs = leaving[a]
                let ins = entering[b]
                let x1 = spread outs.Length (outs |> List.findIndex (fun k -> k = key)) s
                let x2 = spread ins.Length (ins |> List.findIndex (fun k -> k = key)) t

                let through =
                    match chains.TryGetValue key with
                    | true, chain ->
                        chain
                        |> List.collect (fun v ->
                            let l = layerOf[v]
                            [ { X = cx v; Y = layerTops[l] }; { X = cx v; Y = layerTops[l] + layerHeights[l] } ])
                    | _ -> []

                { From = e.From
                  To = e.To
                  Points = [ { X = x1; Y = s.Y + s.Height } ] @ through @ [ { X = x2; Y = t.Y } ] })

        { Width = width
          Height = height
          Nodes = groupIds |> List.map (fun g -> placed[g]) |> List.sortBy (fun n -> n.Layer, n.X)
          Edges = routed }
