/// Queries that run when they are enumerated, as Entity Framework Core's are: catalog `query` rules name them (a
/// DbSet<T>, the result of FromSqlRaw), LINQ builds on them, and a call that enumerates one runs it on the
/// database. The query is followed back through the method's data flow, so a query built in another method and
/// returned from it is not seen.
module Floorplan.Analysis.Queries

open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog

/// Where an enumerated query comes from.
type QueryOrigin =
    { Rule: Rule
      /// The call that made the query, when a rule names it (FromSqlRaw): its offset and method. The rule's
      /// payload is an argument of it.
      Maker: (int * MethodRef) option
      /// What made the query, for people: the call, field or parameter.
      Description: string }

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

/// The query the values come from, if any, in method `m` with data flow `flow`.
let rec private origin (matcher: Matcher) (flow: Flow) (m: MethodDef) (depth: int) (values: Values) : QueryOrigin option =
    if depth > 24 then
        None
    else
        values |> Set.toList |> List.tryPick (one matcher flow m depth)

and private one (matcher: Matcher) (flow: Flow) (m: MethodDef) (depth: int) (v: Value) : QueryOrigin option =
    let ofType (t: string) (description: string) =
        matcher.QueryType(generic t)
        |> Option.map (fun rule ->
            { Rule = rule
              Maker = None
              Description = description })

    match v with
    | Pushed offset ->
        match flow.InstructionAt offset with
        | Some { Op = (Call called | CallVirt called) } ->
            match matcher.Query called with
            | Some rule when rule.Members <> [ "*" ] ->
                Some
                    { Rule = rule
                      Maker = Some(offset, called)
                      Description = called.Display }
            | _ ->
                match ofType called.ReturnType called.Display with
                | Some q -> Some q
                | None ->
                    // A call that builds on its receiver or first argument and returns the query unrun: Where,
                    // OrderBy, Include, AsNoTracking, AsEnumerable, DbSet's AsAsyncEnumerable.
                    match flow.OperandsAt offset with
                    | source :: _ when deferred.Contains(generic called.ReturnType) ->
                        origin matcher flow m (depth + 1) source
                    | _ -> None
        | Some { Op = (LdFld f | LdSFld f) } -> ofType f.FieldType f.Key
        | Some { Op = (CastClass _ | IsInst _) } ->
            match flow.OperandsAt offset with
            | source :: _ -> origin matcher flow m (depth + 1) source
            | [] -> None
        | _ -> None
    | Argument i ->
        let index = if m.IsStatic then i else i - 1

        match List.tryItem index m.Ref.ParameterTypes with
        | Some t when index >= 0 ->
            let name = List.tryItem index m.ParameterNames |> Option.filter (fun n -> n <> "") |> Option.defaultValue $"argument {index}"
            ofType t $"parameter {name} of {m.Ref.Display}"
        | _ -> None
    | _ -> None

/// The query that a call enumerates, and so runs: `foreach` over it (GetEnumerator), a LINQ call that returns
/// something other than a sequence (ToList, First, Count, Any), or a collection made from it.
let enumerated (matcher: Matcher) (flow: Flow) (m: MethodDef) (offset: int) (declared: MethodRef) (isNewObj: bool) : QueryOrigin option =
    let source =
        match flow.OperandsAt offset with
        | first :: _ -> Some first
        | [] -> None

    let enumerates =
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

    if enumerates then
        source |> Option.bind (origin matcher flow m 0)
    else
        None

/// The query a call's first operand is (an EF Core call that runs a query, such as ToListAsync), if any.
let ofFirstOperand (matcher: Matcher) (flow: Flow) (m: MethodDef) (offset: int) : QueryOrigin option =
    match flow.OperandsAt offset with
    | first :: _ -> origin matcher flow m 0 first
    | [] -> None
