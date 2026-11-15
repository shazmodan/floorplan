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
