/// Intra-procedural data flow over a method body: for every instruction, the possible producers of each value
/// it pops. Locals and arguments are transparent, so a value read from a local points at whatever was stored.
/// This is the base for payload recovery (plan section 5) and request-data tracking (section 6.3).
module Floorplan.IL.DataFlow

open System.Collections.Generic

/// Where a value on the evaluation stack came from.
type Value =
    /// Pushed by the instruction at this offset.
    | Pushed of offset: int
    /// The incoming value of an argument (0 is `this` for instance methods).
    | Argument of index: int
    /// The exception object at the start of a catch or filter block.
    | CaughtException
    /// The address of a local; reads through it see the local's value after the call that filled it.
    | LocalAddress of local: int * offset: int

type Values = Set<Value>

/// How many values an instruction pops and pushes.
let stackEffect (op: Op) : int * int =
    match op with
    | Nop
    | Br _
    | Leave _
    | EndFinally
    | Rethrow
    | Constrained _ -> 0, 0
    | LdArg _
    | LdArgA _
    | LdLoc _
    | LdLocA _
    | LdStr _
    | LdcI4 _
    | LdcI8 _
    | LdcR _
    | LdNull
    | LdSFld _
    | LdSFldA _
    | LdToken
    | LdFtn _ -> 0, 1
    | StArg _
    | StLoc _
    | StSFld _
    | BrTrue _
    | BrFalse _
    | Switch _
    | Pop
    | Throw
    | InitObj _ -> 1, 0
    | LdFld _
    | LdFldA _
    | Box _
    | UnboxAny _
    | CastClass _
    | IsInst _
    | LdLen
    | NewArr _
    | LdVirtFtn _ -> 1, 1
    | StFld _
    | BrCmp _ -> 2, 0
    | Ceq
    | Cgt
    | Clt
    | LdElem -> 2, 1
    | StElem -> 3, 0
    | Dup -> 1, 2
    | Ret -> 0, 0 // the return value, if any, is popped; handled specially below
    | Call m
    | CallVirt m -> m.ParameterCount + (if m.HasThis then 1 else 0), (if m.ReturnsValue then 1 else 0)
    | NewObj m -> m.ParameterCount, 1
    | Other(_, pops, pushes) -> pops, pushes

type private State =
    { Stack: Values list // top first
      Locals: Map<int, Values>
      Args: Map<int, Values> }

let private join (a: State) (b: State) =
    let rec stacks (x: Values list) (y: Values list) =
        match x, y with
        | h1 :: t1, h2 :: t2 -> Set.union h1 h2 :: stacks t1 t2
        | [], ys -> ys
        | xs, [] -> xs

    let maps (x: Map<int, Values>) (y: Map<int, Values>) =
        y |> Map.fold (fun acc k v -> acc |> Map.add k (match acc.TryFind k with Some e -> Set.union e v | None -> v)) x

    { Stack = stacks a.Stack b.Stack
      Locals = maps a.Locals b.Locals
      Args = maps a.Args b.Args }

/// The result: for each instruction offset, the producers of each popped value, in push order (the first
/// argument of a call first).
type Flow =
    { Operands: IReadOnlyDictionary<int, Values list>
      /// For each instruction offset that stores to a local, the local index.
      Body: MethodBody }

    member f.OperandsAt(offset: int) =
        match f.Operands.TryGetValue offset with
        | true, v -> v
        | _ -> []

    member f.InstructionAt(offset: int) =
        let i = Instruction.indexOfOffset f.Body.Instructions offset
        if i >= 0 then Some f.Body.Instructions[i] else None

let analyze (returnsValue: bool) (argumentCount: int) (body: MethodBody) : Flow =
    let instrs = body.Instructions
    let indexOf = Dictionary<int, int>()
    instrs |> Array.iteri (fun i ins -> indexOf[ins.Offset] <- i)

    let operands = Dictionary<int, Values list>()

    let initialArgs =
        [ 0 .. argumentCount - 1 ] |> List.map (fun i -> i, Set.singleton (Argument i)) |> Map.ofList

    let entryStates = Dictionary<int, State>()
    let work = Queue<int>()

    let enqueue (index: int) (state: State) =
        match entryStates.TryGetValue index with
        | true, existing ->
            let merged = join existing state

            if merged <> existing then
                entryStates[index] <- merged
                work.Enqueue index
        | _ ->
            entryStates[index] <- state
            work.Enqueue index

    let start = { Stack = []; Locals = Map.empty; Args = initialArgs }

    if instrs.Length > 0 then
        enqueue 0 start

    // Handler entries see an empty stack, or the exception for catch and filter blocks.
    for h in body.Handlers do
        let handlerStack = match h.Kind with Catch | Filter -> [ Set.singleton CaughtException ] | _ -> []

        match indexOf.TryGetValue h.HandlerStart with
        | true, i -> enqueue i { start with Stack = handlerStack }
        | _ -> ()

        match h.FilterStart with
        | Some f ->
            match indexOf.TryGetValue f with
            | true, i -> enqueue i { start with Stack = [ Set.singleton CaughtException ] }
            | _ -> ()
        | None -> ()

    let mutable guard = 0

    while work.Count > 0 && guard < 200_000 do
        guard <- guard + 1
        let startIndex = work.Dequeue()
        let mutable state = entryStates[startIndex]
        let mutable i = startIndex
        let mutable go = true

        while go && i < instrs.Length do
            let ins = instrs[i]
            let pops, pushes = stackEffect ins.Op

            let pops =
                match ins.Op with
                | Ret -> if returnsValue && not state.Stack.IsEmpty then 1 else 0
                | _ -> pops

            let popped = state.Stack |> List.truncate pops
            let rest = state.Stack |> List.skip (min pops state.Stack.Length)
            let inPushOrder = List.rev popped

            operands[ins.Offset] <-
                match operands.TryGetValue ins.Offset with
                | true, existing when existing.Length = inPushOrder.Length -> List.map2 Set.union existing inPushOrder
                | _ -> inPushOrder

            let produced = Set.singleton (Pushed ins.Offset)

            let next =
                match ins.Op with
                | LdArg n ->
                    { state with Stack = (state.Args.TryFind n |> Option.defaultValue (Set.singleton (Argument n))) :: rest }
                | StArg n ->
                    { state with Stack = rest; Args = state.Args.Add(n, (match popped with v :: _ -> v | [] -> Set.empty)) }
                | LdLoc n ->
                    { state with Stack = (state.Locals.TryFind n |> Option.defaultValue Set.empty) :: rest }
                | StLoc n ->
                    { state with Stack = rest; Locals = state.Locals.Add(n, (match popped with v :: _ -> v | [] -> Set.empty)) }
                | LdLocA n ->
                    // After the address escapes, the local holds whatever the callee wrote.
                    let address = Set.singleton (LocalAddress(n, ins.Offset))
                    { state with Stack = address :: rest; Locals = state.Locals.Add(n, address) }
                | Dup ->
                    let v = match popped with v :: _ -> v | [] -> Set.empty
                    { state with Stack = v :: v :: rest }
                | _ ->
                    let pushed = List.replicate pushes produced
                    { state with Stack = pushed @ rest }

            state <- next

            let succ (offset: int) =
                match indexOf.TryGetValue offset with
                | true, t -> enqueue t state
                | _ -> ()

            match ins.Op with
            | Br t -> succ t; go <- false
            | Leave t -> succ t; go <- false
            | BrTrue t
            | BrFalse t
            | BrCmp(_, t) -> succ t
            | Switch ts -> ts |> List.iter succ
            | Ret
            | Throw
            | Rethrow
            | EndFinally -> go <- false
            | _ -> ()

            if go then
                i <- i + 1

                // A branch target is a block of its own: merge into it and continue from its merged state.
                if i < instrs.Length && entryStates.ContainsKey i then
                    enqueue i state
                    go <- false

    { Operands = operands :> IReadOnlyDictionary<int, Values list>
      Body = body }

/// Data flow for a method definition, or None without a body.
let ofMethod (m: MethodDef) =
    m.Body.Value
    |> Option.map (fun body ->
        let argCount = m.Ref.ParameterCount + (if m.IsStatic then 0 else 1)
        analyze m.Ref.ReturnsValue argCount body)
