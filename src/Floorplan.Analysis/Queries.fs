/// Queries that run when they are enumerated, as Entity Framework Core's are: catalog `query` rules name them (a
/// DbSet<T>, the result of FromSqlRaw), LINQ builds on them, and a call that enumerates one runs it on the
/// database. The query is followed back through the method's data flow, and into the methods it calls that
/// return a sequence (a repository's `IQueryable<T> All() => db.Orders`), with their parameters standing for what
/// the call passes. A query passed to a method that enumerates the parameter it is passed as runs at that call.
module Floorplan.Analysis.Queries

open System.Collections.Generic
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog

/// What following a query needs from the analysis.
type Context =
    { Matcher: Matcher
      /// A method's definition and data flow, when its body is read (not the shared framework's).
      Body: MethodRef -> (MethodDef * Flow) option
      /// The methods a call at an offset of a method can run.
      Targets: MethodRef -> int -> MethodRef list }

/// Where an enumerated query comes from.
type QueryOrigin =
    { Rule: Rule
      /// The call that made the query, when a rule names it (FromSqlRaw): the data flow of the method it is in,
      /// its offset and the method called. The rule's payload is an argument of it.
      Maker: (Flow * int * MethodRef) option
      /// The calls that returned the query, innermost first: the data flow of each caller and the call's offset.
      /// A payload that is a parameter of the maker's method is what these calls pass.
      Calls: (Flow * int) list
      /// What made the query, for people: the call, field or parameter, and the methods that returned it.
      Description: string }

/// A method being followed: the one with the enumerating call, or one it calls, at that call.
type private Frame =
    { Def: MethodDef
      Flow: Flow
      Caller: (Frame * int) option }

    member f.Calls(key: string) =
        f.Def.Ref.Key = key || (match f.Caller with Some(c, _) -> c.Calls key | None -> false)

/// A type without its generic arguments: "System.Linq.IQueryable`1<!!0>" -> "System.Linq.IQueryable`1".
let private generic (t: string) =
    match t.IndexOf '<' with
    | -1 -> t
    | i -> t.Substring(0, i)

/// The sequences LINQ returns without running them: a query stays a query through them.
let private deferred =
    set
        [ "System.Collections.Generic.IEnumerable`1"
          "System.Collections.Generic.IAsyncEnumerable`1"
          "System.Linq.IOrderedEnumerable`1"
          "System.Linq.IQueryable`1"
          "System.Linq.IQueryable"
          "System.Linq.IOrderedQueryable`1"
          "System.Linq.IOrderedQueryable"
          // Entity Framework Core's Include and ThenInclude.
          "Microsoft.EntityFrameworkCore.Query.IIncludableQueryable`2" ]

let private linq =
    set [ "System.Linq.Enumerable"; "System.Linq.Queryable"; "System.Linq.AsyncEnumerable" ]

/// How far a query is followed, in values and in nested calls.
let private maxDepth = 24
let private maxCalls = 4

let rec private callDepth (f: Frame) =
    match f.Caller with
    | Some(c, _) -> 1 + callDepth c
    | None -> 0

/// The query the values come from, if any, in the method of `frame`.
let rec private origin (ctx: Context) (frame: Frame) (depth: int) (values: Values) : QueryOrigin option =
    if depth > maxDepth then
        None
    else
        values |> Set.toList |> List.tryPick (one ctx frame depth)

and private one (ctx: Context) (frame: Frame) (depth: int) (v: Value) : QueryOrigin option =
    let flow = frame.Flow

    let ofType (t: string) (description: string) =
        ctx.Matcher.QueryType(generic t)
        |> Option.map (fun rule ->
            { Rule = rule
              Maker = None
              Calls = []
              Description = description })

    match v with
    | Pushed offset ->
        match flow.InstructionAt offset with
        | Some { Op = (Call called | CallVirt called) } ->
            match ctx.Matcher.Query called with
            | Some rule when rule.Members <> [ "*" ] ->
                Some
                    { Rule = rule
                      Maker = Some(flow, offset, called)
                      Calls = []
                      Description = called.Display }
            | _ ->
                match ofType called.ReturnType called.Display with
                | Some q -> Some q
                | None when deferred.Contains(generic called.ReturnType) ->
                    // A call that builds on its receiver or first argument and returns the query unrun: Where,
                    // OrderBy, Include, AsNoTracking, AsEnumerable, DbSet's AsAsyncEnumerable.
                    let built =
                        match flow.OperandsAt offset with
                        | source :: _ -> origin ctx frame (depth + 1) source
                        | [] -> None

                    // Else a method with a body that returns a query: what it returns, at this call.
                    built |> Option.orElseWith (fun () -> returned ctx frame depth offset called)
                | None -> None
        | Some { Op = (LdFld f | LdSFld f) } -> ofType f.FieldType f.Key
        | Some { Op = (CastClass _ | IsInst _) } ->
            match flow.OperandsAt offset with
            | source :: _ -> origin ctx frame (depth + 1) source
            | [] -> None
        | _ -> None
    | Argument i ->
        let m = frame.Def
        let index = if m.IsStatic then i else i - 1

        let typed =
            match List.tryItem index m.Ref.ParameterTypes with
            | Some t when index >= 0 ->
                let name = List.tryItem index m.ParameterNames |> Option.filter (fun n -> n <> "") |> Option.defaultValue $"argument {index}"
                ofType t $"parameter {name} of {m.Ref.Display}"
            | _ -> None

        // In a called method, an argument is what the call passes.
        typed
        |> Option.orElseWith (fun () ->
            match frame.Caller with
            | Some(caller, offset) ->
                match List.tryItem i (caller.Flow.OperandsAt offset) with
                | Some passed -> origin ctx caller (depth + 1) passed
                | None -> None
            | None -> None)
    | _ -> None

/// The query a method called at `offset` returns, from the values its `ret` instructions return.
and private returned (ctx: Context) (frame: Frame) (depth: int) (offset: int) (called: MethodRef) : QueryOrigin option =
    if callDepth frame >= maxCalls then
        None
    else
        let targets =
            match ctx.Targets frame.Def.Ref offset with
            | [] -> [ called ]
            | ts -> ts

        targets
        |> List.filter (fun t -> not (frame.Calls t.Key))
        |> List.tryPick (fun target ->
            ctx.Body target
            |> Option.bind (fun (def, flow) ->
                let inner =
                    { Def = def
                      Flow = flow
                      Caller = Some(frame, offset) }

                flow.Body.Instructions
                |> Array.filter (fun ins -> ins.Op = Ret)
                |> Array.tryPick (fun ret ->
                    match flow.OperandsAt ret.Offset with
                    | value :: _ -> origin ctx inner (depth + 1) value
                    | [] -> None)
                |> Option.map (fun q ->
                    { q with
                        Calls = q.Calls @ [ frame.Flow, offset ]
                        Description = $"{q.Description}, returned by {target.Display}" })))

let private top (m: MethodDef) (flow: Flow) = { Def = m; Flow = flow; Caller = None }

/// Whether a call enumerates its first operand: `foreach` over it (GetEnumerator), a LINQ call that returns
/// something other than a sequence (ToList, First, Count, Any), or a collection made from it.
let private enumerates (declared: MethodRef) (isNewObj: bool) =
    if isNewObj then
        // new List<T>(query), new HashSet<T>(query), ...
        declared.Type.Namespace = "System.Collections.Generic"
        && (match declared.ParameterTypes with
            | first :: _ -> generic first = "System.Collections.Generic.IEnumerable`1"
            | [] -> false)
    elif declared.HasThis then
        (declared.Name = "GetEnumerator" || declared.Name = "GetAsyncEnumerator") && declared.ParameterCount <= 1
    else
        linq.Contains declared.Type.FullName
        && declared.ParameterCount > 0
        && not (deferred.Contains(generic declared.ReturnType))

/// The query that a call enumerates, and so runs.
let enumerated (ctx: Context) (flow: Flow) (m: MethodDef) (offset: int) (declared: MethodRef) (isNewObj: bool) : QueryOrigin option =
    if enumerates declared isNewObj then
        match flow.OperandsAt offset with
        | first :: _ -> origin ctx (top m flow) 0 first
        | [] -> None
    else
        None

/// The query the values a method uses are, if any.
let ofValues (ctx: Context) (flow: Flow) (m: MethodDef) (values: Values) : QueryOrigin option = origin ctx (top m flow) 0 values

/// The query a call's first operand is (an EF Core call that runs a query, such as ToListAsync), if any.
let ofFirstOperand (ctx: Context) (flow: Flow) (m: MethodDef) (offset: int) : QueryOrigin option =
    match flow.OperandsAt offset with
    | first :: _ -> ofValues ctx flow m first
    | [] -> None

/// A parameter that a method enumerates, so that a query passed to it runs there:
/// `void Print(IEnumerable<Order> orders) { foreach (var o in orders) ... }`, or a method it passes the parameter to.
type EnumeratedParameter =
    { /// The parameter's argument index: 0 is `this` in an instance method.
      Argument: int
      /// The call that enumerates it.
      Enumerator: MethodRef
      /// The method called and its parameter's name, then each method the parameter is passed on to, ending with
      /// the one that enumerates it.
      Path: (MethodRef * string) list }

/// Whether a method's parameter at an argument index is a sequence a query could be passed as (IEnumerable<T>,
/// IQueryable<T>), and not of a type a query rule names, whose enumeration is found in the method itself.
let private sequenceParameter (ctx: Context) (m: MethodRef) (i: int) =
    let index = if m.HasThis then i - 1 else i

    index >= 0
    && match List.tryItem index m.ParameterTypes with
       | Some t -> deferred.Contains(generic t) && (ctx.Matcher.QueryType(generic t)).IsNone
       | None -> false

let private hasSequenceParameter (ctx: Context) (m: MethodRef) =
    let first = if m.HasThis then 1 else 0
    List.init m.ParameterCount (fun i -> i + first) |> List.exists (sequenceParameter ctx m)

let private parameterName (m: MethodDef) (i: int) =
    let index = if m.IsStatic then i else i - 1

    List.tryItem index m.ParameterNames
    |> Option.filter (fun n -> n <> "")
    |> Option.defaultValue $"argument {index}"

/// The method's parameter that the values are, as is or through LINQ that builds on it unrun (Where, OrderBy).
let rec private parameterOf (flow: Flow) (depth: int) (values: Values) : int option =
    if depth > maxDepth then
        None
    else
        values
        |> Set.toList
        |> List.tryPick (function
            | Argument i -> Some i
            | Pushed offset ->
                let source () =
                    match flow.OperandsAt offset with
                    | first :: _ -> parameterOf flow (depth + 1) first
                    | [] -> None

                match flow.InstructionAt offset with
                | Some { Op = (Call called | CallVirt called) } when deferred.Contains(generic called.ReturnType) -> source ()
                | Some { Op = (CastClass _ | IsInst _) } -> source ()
                | _ -> None
            | _ -> None)

/// The sequence parameters each method enumerates, worked out from its body (outside the shared framework) and
/// the bodies of the methods it passes them to. Each method is read once.
let enumeratedParameters (ctx: Context) : MethodRef -> EnumeratedParameter list =
    let known = Dictionary<string, EnumeratedParameter list>()

    let rec ofMethod (m: MethodRef) =
        match known.TryGetValue m.Key with
        | true, r -> r
        | _ when not (hasSequenceParameter ctx m) -> []
        | _ ->
            // A method that passes the parameter back to itself adds nothing on the way round.
            known[m.Key] <- []

            let r =
                match ctx.Body m with
                | Some(def, flow) -> inBody def flow
                | None -> []

            known[m.Key] <- r
            r

    and inBody (def: MethodDef) (flow: Flow) =
        let parameter (values: Values) =
            parameterOf flow 0 values |> Option.filter (sequenceParameter ctx def.Ref)

        let here (i: int) = def.Ref, parameterName def i

        flow.Body.Instructions
        |> Array.toList
        |> List.collect (fun ins ->
            let call =
                match ins.Op with
                | Call d
                | CallVirt d -> Some(d, false)
                | NewObj d -> Some(d, true)
                | _ -> None

            match call with
            | None -> []
            | Some(declared, isNewObj) ->
                let operands = flow.OperandsAt ins.Offset

                if enumerates declared isNewObj then
                    match operands |> List.tryHead |> Option.bind parameter with
                    | Some i ->
                        [ { Argument = i
                            Enumerator = declared
                            Path = [ here i ] } ]
                    | None -> []
                else
                    let targets =
                        match ctx.Targets def.Ref ins.Offset with
                        | [] -> [ declared ]
                        | ts -> ts

                    targets
                    |> List.collect ofMethod
                    |> List.choose (fun p ->
                        let operand = if isNewObj then p.Argument - 1 else p.Argument

                        List.tryItem operand operands
                        |> Option.bind parameter
                        |> Option.map (fun i -> { p with Argument = i; Path = here i :: p.Path })))
        |> List.distinctBy (fun p -> p.Argument)

    ofMethod
