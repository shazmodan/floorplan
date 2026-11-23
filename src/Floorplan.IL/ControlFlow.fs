/// Control flow within one method body: successors of each instruction, reachability, and control dependence
/// (which branch decides whether an instruction runs).
module Floorplan.IL.ControlFlow

open System.Collections.Generic

type Graph =
    { Body: MethodBody
      /// Successor instruction indices of each instruction index.
      Successors: int list[]
      IndexOf: IReadOnlyDictionary<int, int> }

let build (body: MethodBody) : Graph =
    let instrs = body.Instructions
    let indexOf = Dictionary<int, int>()
    instrs |> Array.iteri (fun i ins -> indexOf[ins.Offset] <- i)

    let at (offset: int) =
        match indexOf.TryGetValue offset with
        | true, i -> [ i ]
        | _ -> []

    let next i = if i + 1 < instrs.Length then [ i + 1 ] else []

    let successors =
        instrs
        |> Array.mapi (fun i ins ->
            match ins.Op with
            | Br t
            | Leave t -> at t
            | BrTrue t
            | BrFalse t
            | BrCmp(_, t) -> next i @ at t
            | Switch ts -> next i @ (ts |> List.collect at)
            | Ret
            | Throw
            | Rethrow
            | EndFinally -> []
            | _ -> next i)

    { Body = body
      Successors = successors
      IndexOf = indexOf }

/// Instruction indices reachable from `start` (inclusive) without passing through `blocked`.
let reachable (g: Graph) (start: int) (blocked: int) : HashSet<int> =
    let seen = HashSet<int>()
    let stack = Stack<int>()

    if start <> blocked then
        stack.Push start

    while stack.Count > 0 do
        let i = stack.Pop()

        if seen.Add i then
            for s in g.Successors[i] do
                if s <> blocked && not (seen.Contains s) then
                    stack.Push s

    seen

/// The branch successors of a conditional branch: (taken target, fall-through) for brtrue/brfalse/brcmp,
/// or every case target for a switch.
let branchTargets (g: Graph) (index: int) : int list =
    g.Successors[index]

/// Whether the instruction at `target` runs on some but not all ways out of the conditional branch at
/// `branch`: it is reachable from one successor and not from another (without re-entering the branch).
let isControlledBy (g: Graph) (branch: int) (target: int) =
    match g.Successors[branch] with
    | []
    | [ _ ] -> false
    | succs ->
        let reach = succs |> List.map (fun s -> (reachable g s branch).Contains target)
        List.contains true reach && List.contains false reach

/// Which successor of `branch` leads to `target`: the index in Successors, if exactly one does.
let directionTo (g: Graph) (branch: int) (target: int) =
    let reach =
        g.Successors[branch] |> List.map (fun s -> (reachable g s branch).Contains target)

    match reach |> List.indexed |> List.filter snd with
    | [ (i, _) ] -> Some i
    | _ -> None

/// The locals each instruction may still read before writing them, by instruction index. A local whose address is
/// taken, or that an exception handler reads, counts as live everywhere: reads through the address and the jump
/// into a handler are not followed.
let liveLocals (g: Graph) : Set<int>[] =
    let instrs = g.Body.Instructions
    let n = instrs.Length

    let inHandler (offset: int) =
        g.Body.Handlers
        |> List.exists (fun h ->
            (offset >= h.HandlerStart && offset < h.HandlerEnd)
            || (match h.FilterStart with Some f -> offset >= f && offset < h.HandlerStart | None -> false))

    let always =
        instrs
        |> Array.choose (fun ins ->
            match ins.Op with
            | LdLocA l -> Some l
            | LdLoc l when inHandler ins.Offset -> Some l
            | _ -> None)
        |> Set.ofArray

    let live = Array.create n Set.empty
    let predecessors = Array.init n (fun _ -> ResizeArray<int>())

    g.Successors |> Array.iteri (fun i succs -> for s in succs do predecessors[s].Add i)

    let work = Queue<int>(seq { n - 1 .. -1 .. 0 })
    let queued = HashSet<int>(seq { 0 .. n - 1 })

    while work.Count > 0 do
        let i = work.Dequeue()
        queued.Remove i |> ignore
        let after = g.Successors[i] |> List.fold (fun acc s -> Set.union acc live[s]) always

        let before =
            match instrs[i].Op with
            | LdLoc l -> after.Add l
            | StLoc l when not (always.Contains l) -> after.Remove l
            | _ -> after

        if before <> live[i] then
            live[i] <- before

            for p in predecessors[i] do
                if queued.Add p then work.Enqueue p

    live
