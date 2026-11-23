/// Form C (plan section 6.4): the route template and the handler are handed to the platform's own routing,
/// and the catalog says which argument is which. The cheapest case and the most reliable.
module Floorplan.Analysis.PlatformRouting

open System
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog

/// A registration call found in code.
type Registration =
    { Caller: MethodDef
      Offset: int
      Index: int
      Rule: Rule
      Declared: MethodRef
      Flow: Flow }

/// The method a delegate or function value passed as an argument will run, unwrapping compiler-generated
/// wrappers to the first user method they call.
let rec handlerOf (set: AssemblySet) (flow: Flow) (values: Values) : MethodRef option =
    match Set.toList values with
    | [ Pushed offset ] ->
        match flow.InstructionAt offset with
        | Some { Op = NewObj ctor } ->
            // new Func<...>(target, ldftn M): the delegate runs M.
            let fromFtn =
                flow.OperandsAt offset
                |> List.tryPick (fun vs ->
                    match Set.toList vs with
                    | [ Pushed o ] ->
                        match flow.InstructionAt o with
                        | Some { Op = (LdFtn m | LdVirtFtn m) } -> Some m
                        | _ -> None
                    | _ -> None)

            match fromFtn with
            | Some m -> Some m
            | None ->
                // A function object: its Invoke runs.
                match set.TryType ctor.Type with
                | Some(def, _) ->
                    def.Methods |> List.tryFind (fun m -> m.Ref.Name = "Invoke" && not m.IsAbstract) |> Option.map (fun m -> m.Ref)
                | None -> None
        | Some { Op = LdSFld f } ->
            // A cached function object: its Invoke runs.
            match set.TryType f.Type with
            | Some(def, _) ->
                def.Methods |> List.tryFind (fun m -> m.Ref.Name = "Invoke" && not m.IsAbstract) |> Option.map (fun m -> m.Ref)
            | None -> None
        | Some { Op = (CastClass _ | Box _) } ->
            match flow.OperandsAt offset with
            | [ inner ] -> handlerOf set flow inner
            | _ -> None
        | _ -> None
    | many when many.Length > 1 ->
        // A delegate cached in a static field (C#'s `<>9__0_4 ?? (<>9__0_4 = new Func<...>(...))`): the field's
        // value or the one just made. The values that name a method must agree on it.
        match many |> List.choose (fun v -> handlerOf set flow (Set.singleton v)) |> List.distinctBy (fun m -> m.Key) with
        | [ m ] -> Some m
        | _ -> None
    | _ -> None

let private isGenerated (set: AssemblySet) (m: MethodRef) =
    match set.TryMethod m with
    | Some(def, owner) -> def.IsCompilerGenerated || owner.IsCompilerGenerated
    | None -> false

/// Follows a compiler-generated wrapper (a lambda or delegate stub) to the first user method it calls.
let rec unwrap (set: AssemblySet) (isUser: MethodRef -> bool) (depth: int) (m: MethodRef) : MethodRef =
    if depth > 4 || not (isGenerated set m) then
        m
    else
        match set.TryMethod m with
        | Some(def, _) ->
            match def.Body.Value with
            | Some body ->
                let calls =
                    body.Instructions
                    |> Array.choose (fun i ->
                        match i.Op with
                        | Call t
                        | CallVirt t when isUser t && not (MethodRef.isConstructor t) -> Some t
                        | _ -> None)
                    |> Array.toList

                match calls with
                | first :: _ -> unwrap set isUser (depth + 1) first
                | [] -> m
            | None -> m
        | None -> m

let normalizeRoute (parts: string list) =
    let joined =
        parts
        |> List.map (fun p -> p.Trim('/'))
        |> List.filter (fun p -> p <> "")
        |> String.concat "/"

    "/" + joined

/// Predicates for a route template: one per segment, parameters for {name} and catch-alls.
let templatePredicates (route: string) (location: Location option) : Predicate list =
    let segments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)

    let segmentPredicates =
        segments
        |> Array.mapi (fun i s ->
            let isParameter = (s.StartsWith "{" && s.EndsWith "}") || s.StartsWith "*" || s.StartsWith ":"

            { Part = RequestPart.PathSegment
              Operator = if isParameter then PredicateOperator.Parameter else PredicateOperator.Equals
              Index = Some i
              Value = s
              Location = location })
        |> Array.toList

    let catchAll = segments |> Array.exists (fun s -> s.StartsWith "{*" || s.StartsWith "{**" || s = "*")

    let count =
        if catchAll then
            []
        else
            [ { Part = RequestPart.SegmentCount
                Operator = PredicateOperator.Equals
                Index = None
                Value = string segments.Length
                Location = location } ]

    segmentPredicates @ count

/// Finds registrations in the given methods.
let registrations (matcher: Matcher) (methods: MethodDef list) : Registration list =
    methods
    |> List.collect (fun m ->
        match m.Body.Value with
        | None -> []
        | Some body ->
            let flow = lazy (DataFlow.ofMethod m)

            body.Instructions
            |> Array.mapi (fun i ins -> i, ins)
            |> Array.choose (fun (i, ins) ->
                match ins.Op with
                | Call d
                | CallVirt d ->
                    match matcher.Registration d, flow.Value with
                    | Some rule, Some f ->
                        Some
                            { Caller = m
                              Offset = ins.Offset
                              Index = i
                              Rule = rule
                              Declared = d
                              Flow = f }
                    | _ -> None
                | _ -> None)
            |> Array.toList)

/// Whether a builder received as a parameter of this type may be a route group, whose prefix the caller chose.
let mayBeGroup (parameterType: string) =
    parameterType.StartsWith "Microsoft.AspNetCore.Routing.RouteGroupBuilder"
    || parameterType.StartsWith "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"

/// Whether a call returns the object it is given, by its signature alone: a generic method whose result is its
/// first parameter's type argument (`TBuilder AddEndpointFilter<TBuilder>(this TBuilder builder, ...)`, and so
/// `WithTags`, `RequireAuthorization`) has no other value of that type to return. A convention chained after
/// MapGroup keeps the group.
let returnsItsBuilder (m: MethodRef) =
    not m.HasThis
    && m.GenericArity > 0
    && m.ReturnType.StartsWith "!!"
    && (match m.ParameterTypes with
        | first :: _ -> first = m.ReturnType
        | [] -> false)

/// The route prefix contributed by the builder a registration is called on: MapGroup("/api").MapGet(...). A
/// builder that is a parameter of the registering method and may be a group has a prefix only its callers know.
let rec prefixOf (matcher: Matcher) (evaluator: Values.Evaluator) (flow: Flow) (caller: MethodRef) (depth: int) (builder: Values) : string list option =
    if depth > 8 then
        Some []
    else
        match Set.toList builder with
        | [ Pushed o ] ->
            match flow.InstructionAt o with
            | Some { Op = (Call d | CallVirt d) } ->
                match matcher.Registration d with
                | Some rule when rule.Payload |> List.exists (fun p -> p.Role = "route-prefix") ->
                    let spec = rule.Payload |> List.find (fun p -> p.Role = "route-prefix")
                    let operands = flow.OperandsAt o
                    let index = Values.operandIndex d false spec.Argument

                    match evaluator.Operand(o, index) with
                    | Values.Text prefix ->
                        match operands with
                        | inner :: _ -> prefixOf matcher evaluator flow caller (depth + 1) inner |> Option.map (fun p -> p @ [ prefix ])
                        | [] -> Some [ prefix ]
                    | _ -> None
                | _ when returnsItsBuilder d ->
                    match flow.OperandsAt o with
                    | inner :: _ -> prefixOf matcher evaluator flow caller (depth + 1) inner
                    | [] -> Some []
                | _ -> Some []
            | _ -> Some []
        | [ Argument i ] ->
            let index = if caller.HasThis then i - 1 else i

            match List.tryItem index caller.ParameterTypes with
            | Some t when mayBeGroup t -> None
            | _ -> Some []
        | _ -> Some []
