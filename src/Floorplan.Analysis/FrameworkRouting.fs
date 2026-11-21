/// Routing written inside a dependency (plan sections 6.3 and 6.4, "wherever they occur"): the application's
/// entry point is run symbolically to see what it registers with the platform, then each registered request
/// path is run with request data as input to see which user code handles which request.
module Floorplan.Analysis.FrameworkRouting

open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog
open Floorplan.Analysis.EntryPoints
open Floorplan.Analysis.Symbolic

/// The strings a value stands for: a constant, or the constants a collection or sequence produces. Anything
/// else gives nothing; method names are never guessed.
let private strings (machine: Machine) (state: PathState) (v: SVal) : string list option =
    match v with
    | Const(:? string as s) -> Some [ s ]
    | Ref _ ->
        machine.Enumerate(v, state)
        |> Option.bind (fun items ->
            let texts = items |> List.choose (function Const(:? string as s) -> Some s | _ -> None)
            if texts.Length = items.Length then Some texts else None)
    | _ -> None

/// The HTTP methods an endpoint accepts: from the rule, from an argument, or from metadata attached to the
/// endpoint builder.
let private httpMethods (machine: Machine) (matcher: Matcher) (rule: Rule) (argument: SVal option) (builder: int option) (state: PathState) =
    match rule.HttpMethod with
    | Some m -> Some m
    | None ->
        let fromArgument = argument |> Option.bind (strings machine state) |> Option.defaultValue []

        if System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG" = "1" then
            match argument with
            | Some a -> eprintfn "  methods argument %A enumerates to %A" a (machine.Enumerate(a, state))
            | None -> ()

        let fromMetadata =
            match builder |> Option.bind (fun id -> state.Heap.TryFind id) |> Option.bind (fun o -> o.Fields.TryFind "<metadata>") with
            | Some(Ref list) ->
                match state.Heap.TryFind list with
                | Some l ->
                    l.Elements
                    |> Map.toList
                    |> List.collect (fun (_, item) ->
                        match item with
                        | Ref id ->
                            match state.Heap.TryFind id with
                            | Some o when matcher.Catalog.Rules |> List.exists (fun r -> r.Kind = Metadata && r.Type = o.Type.FullName) ->
                                o.Fields |> Map.toList |> List.collect (fun (_, a) -> strings machine state a |> Option.defaultValue [])
                            | _ -> []
                        | _ -> [])
                | None -> []
            | _ -> []

        match (fromArgument @ fromMetadata) |> List.map (fun m -> m.ToUpperInvariant()) |> List.distinct with
        | [] -> None
        | ms -> Some(String.concat "," ms)

let private pointIn (set: AssemblySet) (m: MethodRef) (offset: int) =
    match set.TryMethod m with
    | Some(def, _) ->
        match def.Body.Value with
        | Some body ->
            let i = Instruction.indexOfOffset body.Instructions offset
            if i >= 0 then Instruction.pointAt body.Instructions i else body.Instructions |> Array.tryPick (fun x -> x.Point)
        | None -> None
    | None -> None

let private entry (ctx: AppContext) (hit: Hit) (forms: RouteForm list) (steps: EvidenceStep list) : EntryPoint option =
    let set = ctx.Program.Set

    let handler, point =
        match hit.Handler with
        | HandlerMethod m -> m, firstPoint set m
        | HandlerSite(m, offset) -> m, pointIn set m offset

    let projectId =
        match set.Origin handler.Type.Assembly with
        | User pid -> pid
        | _ -> ctx.Project.ProjectId

    ctx.Program.Attributor.Attribute(projectId, handler, point)
    |> Option.map (fun unit ->
        let predicates = hit.Predicates |> List.distinct
        let httpMethod, route, completeness = RouteRecovery.assemble predicates

        let route =
            match hit.Handler, route with
            | HandlerMethod m, Some r -> Some(RouteRecovery.nameFromHandler set m r)
            | _ -> route

        // No user function handles the request: name the unit whose code built the library handler.
        let handlerName =
            match hit.Handler with
            | HandlerMethod m -> m.Display
            | HandlerSite _ -> $"{unit.Name} (response built by a library function)"

        { Id = ""
          ApplicationId = ctx.Project.ProjectId
          HandlerUnit = unit.Id
          HandlerMember = handlerName
          Location = point |> Option.map Attribution.toLocation
          Category = EffectCategory.HttpIn
          Predicates = predicates
          HttpMethod = httpMethod
          Route = route
          Completeness = completeness
          Forms = if predicates.IsEmpty then [] else forms
          Evidence =
            { Steps =
                steps
                @ (match hit.Handler with
                   | HandlerSite(m, _) ->
                       [ { Description = $"no user function runs for this request; the response is built by a library function created in {m.Display}"
                           Member = m.Display
                           Assembly = m.Type.Assembly
                           Location = point |> Option.map Attribution.toLocation } ]
                   | HandlerMethod _ -> []) } })

/// Whether anything reachable from the application, in its own code or in a dependency, registers a request
/// handler with the platform or reads request data. Applications that do neither are not run symbolically.
let private receivesRequests (ctx: AppContext) =
    let graph = ctx.Program.GraphFor ctx.Project
    let seen = System.Collections.Generic.HashSet<string>()
    let stack = System.Collections.Generic.Stack<MethodRef>()

    match ctx.Program.Users |> List.tryFind (fun (p, _) -> p.ProjectId = ctx.Project.ProjectId) with
    | Some(_, assembly) ->
        for t in assembly.Types do
            for m in t.Methods do
                stack.Push m.Ref
    | None -> ()

    let mutable found = false

    while not found && stack.Count > 0 do
        let m = stack.Pop()

        if seen.Add m.Key then
            for e in graph.Callees m do
                // Only HTTP registrations route requests: message consumers and hosted services do not.
                let registers =
                    ctx.Matcher.Registration e.Declared |> Option.exists (fun r -> r.Category = Some EffectCategory.HttpIn)

                if registers || (ctx.Matcher.Source e.Declared).IsSome then
                    found <- true
                elif not (seen.Contains e.Target.Key) then
                    stack.Push e.Target

    found

/// Entry points found by running the application symbolically.
let private analyzeOnThisThread (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set

    let entryPoint =
        ctx.Program.Users
        |> List.tryFind (fun (p, _) -> p.ProjectId = ctx.Project.ProjectId)
        |> Option.bind (fun (_, a) -> a.EntryPoint)

    match entryPoint with
    | None -> []
    | Some _ when not (receivesRequests ctx) -> []
    | Some main ->
        let machine = Machine(set, ctx.Matcher, ctx.Program.Recognizers, defaultLimits)
        let args = List.replicate main.ParameterCount Unknown
        machine.Invoke(Registration, main, args, emptyState, 0) |> ignore

        if System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG" = "1" then
            eprintfn "[symbolic] %s: %d registrations after %d steps" ctx.Project.ProjectId machine.Registered.Length machine.Steps

            for r in machine.Registered |> List.truncate 12 do
                match r with
                | Endpoint(rule, template, prefix, methods, handler, _, _, _) ->
                    eprintfn "  endpoint %s template=%A prefix=%A methods=%A handler=%A" rule.Id template prefix methods handler
                | RequestRoot(rule, handler, _, _) -> eprintfn "  root %s handler=%A" rule.Id handler

        let step (description: string) (m: MethodRef, offset: int) =
            { Description = description
              Member = m.Display
              Assembly = m.Type.Assembly
              Location = pointIn set m offset |> Option.map Attribution.toLocation }

        // The same registration reached along several paths is explored once.
        let distinctRegistrations =
            machine.Registered
            |> List.distinctBy (fun r ->
                let handlerKey (h: SVal) =
                    match h with
                    | Fn(m, _) -> m.Key
                    | Ref id -> "ref"
                    | other -> sprintf "%A" other

                match r with
                | Endpoint(rule, template, prefix, methods, handler, builder, state, _) ->
                    // Two endpoints differ when their template or their methods do, even with one delegate type.
                    rule.Id, sprintf "%A|%A|%A" template prefix (httpMethods machine ctx.Matcher rule methods builder state), handlerKey handler
                | RequestRoot(rule, handler, _, _) -> rule.Id, "", handlerKey handler)

        let found =
          distinctRegistrations
          |> List.collect (fun r ->
            match r with
            | Endpoint(rule, template, prefix, methodArgument, handler, builder, state, site) ->
                let methods = httpMethods machine ctx.Matcher rule methodArgument builder state

                let route =
                    match template, prefix with
                    | Const(:? string as t), Some p -> Some(PlatformRouting.normalizeRoute (p @ [ t ]))
                    | _ -> None

                let seed =
                    (methods
                     |> Option.map (fun m ->
                         { Part = RequestPart.Method
                           Operator = PredicateOperator.Equals
                           Index = None
                           Value = m
                           Location = None })
                     |> Option.toList)
                    @ (route |> Option.map (fun r -> PlatformRouting.templatePredicates r None) |> Option.defaultValue [])

                let steps = [ step $"{(fst site).Display} registers an endpoint with the platform (rule {rule.Id})" site ]

                if System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG" = "1" then
                    eprintfn "  explore %s route=%A methods=%A handler=%s" rule.Id route methods (match handler with Fn(m, _) -> m.Display | other -> sprintf "%A" other)

                let hits = machine.Explore(handler, state, seed)

                // A user function found on some path is the handler; library responses on other paths of the same
                // endpoint (error paths, mostly) add nothing.
                let hits =
                    if hits |> List.exists (fun h -> match h.Handler with HandlerMethod _ -> true | _ -> false) then
                        hits |> List.filter (fun h -> match h.Handler with HandlerMethod _ -> true | _ -> false)
                    else
                        hits

                match hits with
                | [] when route.IsSome ->
                    entry ctx { Handler = HandlerSite site; Predicates = seed } [ RouteForm.PlatformRouting ] steps |> Option.toList
                | [] -> []
                | hits ->
                    hits
                    // A library response with no route says too little to report.
                    |> List.filter (fun h -> match h.Handler with HandlerSite _ -> route.IsSome | _ -> true)
                    |> List.choose (fun h -> entry ctx h [ RouteForm.PlatformRouting ] steps)
            | RequestRoot(rule, handler, state, site) ->
                let steps = [ step $"{(fst site).Display} registers a request handler with the platform (rule {rule.Id})" site ]

                let hits = machine.Explore(handler, state, [])

                if System.Environment.GetEnvironmentVariable "FLOORPLAN_DEBUG" = "1" then
                    eprintfn "  explored root %s: %d hits, %d steps (%d in all), exhausted %b" rule.Id hits.Length machine.Steps machine.TotalSteps machine.Exhausted

                    for h in hits do
                        eprintfn "  hit %A with %A" h.Handler (h.Predicates |> List.map (fun p -> p.Part, p.Value))

                hits |> List.choose (fun h -> entry ctx h [ RouteForm.ControlFlow ] steps))

        // User functions handed to a library but reached by no request path: handlers with no route. Only those
        // whose signature carries the request: a library is also handed configuration callbacks (logging, services,
        // options), which run at startup and handle nothing.
        let reached = found |> List.map (fun e -> e.HandlerMember) |> Set.ofList

        let requestTypes =
            ctx.Matcher.Catalog.Rules
            |> List.filter (fun r ->
                (r.Kind = RuleKind.Source && (r.Category = Some EffectCategory.HttpIn || r.Category = Some EffectCategory.Network))
                || (r.Kind = RuleKind.Metadata && r.Payload |> List.exists (fun p -> p.Role = "request-context")))
            |> List.map (fun r -> r.Type)
            |> List.distinct

        let carriesRequest (f: MethodRef) =
            f.ReturnType :: f.ParameterTypes
            |> List.exists (fun t -> requestTypes |> List.exists (fun r -> t.Contains r))

        let unreached =
            if found.IsEmpty then
                []
            else
                machine.HandedOver
                |> List.filter (fun (f, _) -> not (reached.Contains f.Display) && not (f.Name.StartsWith "get_") && carriesRequest f)
                |> List.choose (fun (f, site) ->
                    entry
                        ctx
                        { Handler = HandlerMethod f; Predicates = [] }
                        []
                        [ step $"{f.Display} is handed to {(fst site).Display}; no request path to it was recovered" site ])

        found @ unreached

/// The interpreter recurses once per interpreted call, through several .NET frames each, so it runs on a thread
/// with a stack sized for its depth limit rather than on the caller's.
let analyze (ctx: AppContext) : EntryPoint list =
    let mutable result = Ok []

    let worker =
        System.Threading.Thread(
            (fun () ->
                result <-
                    try
                        Ok(analyzeOnThisThread ctx)
                    with e ->
                        Error e),
            512 * 1024 * 1024
        )

    worker.Start()
    worker.Join()

    match result with
    | Ok entries -> entries
    | Error e -> raise (System.AggregateException("Symbolic routing analysis failed", e))

