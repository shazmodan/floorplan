/// Entry points (plan section 6): user code reachable from an inbound-connection primitive that receives data
/// derived from the request. Handlers come from platform registrations (form C) and from request-data
/// tracking: a handler is a user function invoked on a branch chosen by request data, or, where nothing
/// branches on it, the first user function that receives it.
module Floorplan.Analysis.EntryPoints

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.DataFlow
open Floorplan.Catalog
open Floorplan.Analysis.Taint

/// A handler found by request-data tracking, before routes are recovered.
type FoundHandler =
    { Handler: MethodRef
      /// The method and call instruction where the handler is invoked.
      Caller: MethodDef
      CallIndex: int
      /// Conditional branches on request data that decide whether the call runs (instruction indices).
      Guards: int list
      Tags: Tags }

type AppContext =
    { Project: AnalyzedProject
      Program: EffectAnalysis.Program
      Matcher: Matcher
      /// User methods reachable from the application, by key.
      Methods: IReadOnlyDictionary<string, MethodDef>
      Taint: TaintResult }

let private step (description: string) (m: MethodRef) (point: SourcePoint option) =
    { Description = description
      Member = m.Display
      Assembly = m.Type.Assembly
      Location = point |> Option.map Attribution.toLocation }

let firstPoint (set: AssemblySet) (m: MethodRef) =
    match set.TryMethod m with
    | Some(def, _) ->
        match def.Body.Value with
        | Some body -> body.Instructions |> Array.tryPick (fun i -> i.Point)
        | None -> None
    | None -> None

let private isAccessorOrCtor (m: MethodRef) =
    MethodRef.isConstructor m || m.Name.StartsWith "get_" || m.Name.StartsWith "set_"

/// User methods reachable from an application's own assembly, through user code only.
let reachableUserMethods (program: EffectAnalysis.Program) (project: AnalyzedProject) =
    let set = program.Set
    let result = Dictionary<string, MethodDef>()
    let stack = Stack<MethodRef>()

    match program.Users |> List.tryFind (fun (p, _) -> p.ProjectId = project.ProjectId) with
    | Some(_, assembly) ->
        for t in assembly.Types do
            for m in t.Methods do
                stack.Push m.Ref
    | None -> ()

    while stack.Count > 0 do
        let m = stack.Pop()

        if not (result.ContainsKey m.Key) && set.IsUser m.Type.Assembly then
            match set.TryMethod m with
            | Some(def, _) when def.Body.Value.IsSome ->
                result[m.Key] <- def

                for e in (program.GraphFor project).Callees m do
                    if set.IsUser e.Target.Type.Assembly && not (result.ContainsKey e.Target.Key) then
                        stack.Push e.Target
            | _ -> ()

    result :> IReadOnlyDictionary<string, MethodDef>

/// Instruction offsets whose values feed the given values, within one method (through any instruction).
let rec private contributors (flow: Flow) (depth: int) (acc: HashSet<int>) (values: Values) =
    if depth < 64 then
        for v in values do
            match v with
            | Pushed o when acc.Add o ->
                for operand in flow.OperandsAt o do
                    contributors flow (depth + 1) acc operand
            | _ -> ()

/// The conditional branches of a method whose condition carries request data.
let taintedBranches (ctx: AppContext) (m: MethodDef) : (int * Tags) list =
    match m.Body.Value with
    | None -> []
    | Some body ->
        body.Instructions
        |> Array.mapi (fun i ins -> i, ins)
        |> Array.choose (fun (i, ins) ->
            match ins.Op with
            | BrTrue _
            | BrFalse _
            | BrCmp _
            | Switch _ ->
                let tags = ctx.Taint.OperandTags m.Ref ins.Offset |> Set.unionMany
                if tags.IsEmpty then None else Some(i, tags)
            | _ -> None)
        |> Array.toList

let private userTargets (ctx: AppContext) (m: MethodDef) (ins: Instruction) (declared: MethodRef) =
    let edges =
        (ctx.Program.GraphFor ctx.Project).Callees m.Ref
        |> List.filter (fun e -> e.Offset = ins.Offset && (e.Kind = Direct || e.Kind = Virtual))
        |> List.map (fun e -> e.Target)

    (if edges.IsEmpty then [ declared ] else edges)
    |> List.filter (fun t -> ctx.Methods.ContainsKey t.Key)

/// Calls in `m` that run only on some outcomes of a request-data branch, excluding calls that compute the
/// branch conditions themselves (accessors, active patterns, comparisons).
let guardedCalls (ctx: AppContext) (m: MethodDef) : (int * MethodRef * int list) list =
    match m.Body.Value, DataFlow.ofMethod m with
    | Some body, Some flow ->
        let branches = taintedBranches ctx m

        if branches.IsEmpty then
            []
        else
            let cfg = ControlFlow.build body
            let conditionCalls = HashSet<int>()

            for (b, _) in branches do
                for operand in flow.OperandsAt body.Instructions[b].Offset do
                    contributors flow 0 conditionCalls operand

            body.Instructions
            |> Array.mapi (fun i ins -> i, ins)
            |> Array.collect (fun (i, ins) ->
                match ins.Op with
                | Call d
                | CallVirt d when not (conditionCalls.Contains ins.Offset) && not (isAccessorOrCtor d) ->
                    let guards = branches |> List.map fst |> List.filter (fun b -> ControlFlow.isControlledBy cfg b i)

                    if guards.IsEmpty then
                        [||]
                    else
                        userTargets ctx m ins d
                        |> List.filter (fun t -> not (isAccessorOrCtor t))
                        |> List.map (fun t -> i, t, guards)
                        |> List.toArray
                | _ -> [||])
            |> Array.toList
    | _ -> []

/// Handlers found by request-data tracking (plan section 6.3, step 3).
let trackedHandlers (ctx: AppContext) : FoundHandler list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key
    let unwrap = PlatformRouting.unwrap set isUser 0

    // Structural equality, hashing and comparison the compiler generates for records and unions branch on
    // every field, request data included, but route nothing.
    let structural (m: MethodDef) =
        m.IsCompilerGenerated && (m.Ref.Name = "Equals" || m.Ref.Name = "GetHashCode" || m.Ref.Name = "CompareTo")

    let guarded =
        ctx.Methods.Values
        |> Seq.filter (fun m -> not (structural m))
        |> Seq.collect (fun m ->
            guardedCalls ctx m
            |> List.map (fun (i, t, guards) ->
                let tags =
                    guards
                    |> List.collect (fun g -> ctx.Taint.OperandTags m.Ref m.Body.Value.Value.Instructions[g].Offset)
                    |> Set.unionMany

                { Handler = unwrap t
                  Caller = m
                  CallIndex = i
                  Guards = guards
                  Tags = tags }))
        |> Seq.toList

    // A function that itself dispatches on request data is a router: its own guarded calls are the handlers.
    let routers =
        guarded |> List.map (fun g -> g.Caller.Ref.Key) |> Set.ofList

    let branchHandlers = guarded |> List.filter (fun g -> not (routers.Contains g.Handler.Key))

    // Sources whose data never decides a branch: the first user function that receives the data handles it.
    let dispatching =
        ctx.Methods.Values |> Seq.filter (fun m -> not (taintedBranches ctx m).IsEmpty) |> Seq.map (fun m -> m.Ref.Key) |> Set.ofSeq

    // The methods that create each closure: a source read in a lambda serves the method that made the lambda.
    let creators =
        lazy
            (ctx.Methods.Values
             |> Seq.collect (fun m ->
                 m.Body.Value.Value.Instructions
                 |> Seq.choose (fun i ->
                     match i.Op with
                     | NewObj ctor when ctor.Type <> m.Ref.Type -> Some(ctor.Type.FullName, m.Ref.Key)
                     | _ -> None))
             |> Seq.distinct
             |> Seq.groupBy fst
             |> Seq.map (fun (t, ms) -> t, ms |> Seq.map snd |> List.ofSeq)
             |> Map.ofSeq)

    let receivers =
        ctx.Taint.Sources
        |> List.filter (fun s -> ctx.Methods.ContainsKey s.Method.Key)
        |> List.distinctBy (fun s -> s.Method.Key)
        |> List.collect (fun s ->
            // Everything downstream of the source, through calls that pass request data along, and of the
            // methods creating the closure the source is read in.
            let closureCreators =
                match set.TryType s.Method.Type with
                | Some(t, _) when
                    t.IsCompilerGenerated
                    || set.BaseChain t.Ref |> List.exists (fun b -> b.FullName.StartsWith "Microsoft.FSharp.Core.FSharpFunc`")
                    ->
                    creators.Force() |> Map.tryFind s.Method.Type.FullName |> Option.defaultValue []
                | _ -> []

            let downstream = HashSet<string>(s.Method.Key :: closureCreators)
            let stack = Stack<string>(s.Method.Key :: closureCreators)

            while stack.Count > 0 do
                let k = stack.Pop()

                for e in (ctx.Program.GraphFor ctx.Project).Callees ctx.Methods[k].Ref do
                    if isUser e.Target
                       && ctx.Taint.Parameters.ContainsKey e.Target.Key
                       && downstream.Add e.Target.Key then
                        stack.Push e.Target.Key

            if downstream |> Seq.exists dispatching.Contains then
                []
            else
                let m = ctx.Methods[s.Method.Key]
                let body = m.Body.Value.Value
                let tags = Set.singleton (match s.Rule.RequestPart with Some p -> Part p | None -> if s.Rule.Category = Some EffectCategory.Network then Connection else HttpContext)

                let firstReceiver =
                    body.Instructions
                    |> Array.mapi (fun i ins -> i, ins)
                    |> Array.tryPick (fun (i, ins) ->
                        match ins.Op with
                        | Call d
                        | CallVirt d when not (isAccessorOrCtor d) ->
                            let carries = ctx.Taint.OperandTags m.Ref ins.Offset |> List.exists (fun t -> not t.IsEmpty)

                            if carries then
                                userTargets ctx m ins d
                                |> List.filter (fun t -> not (isAccessorOrCtor t))
                                |> List.tryHead
                                |> Option.map (fun t -> i, t)
                            else
                                None
                        | _ -> None)

                match firstReceiver with
                | Some(i, t) ->
                    [ { Handler = unwrap t
                        Caller = m
                        CallIndex = i
                        Guards = []
                        Tags = tags } ]
                | None ->
                    [ { Handler = m.Ref
                        Caller = m
                        CallIndex = 0
                        Guards = []
                        Tags = tags } ])

    branchHandlers @ receivers

let categoryOf (tags: Tags) =
    if not tags.IsEmpty && tags |> Set.forall (fun t -> t = Connection) then
        EffectCategory.Network
    else
        EffectCategory.HttpIn

/// An entry point for a tracked handler, with no route yet (Phase 3): unresolved.
let unresolvedEntry (ctx: AppContext) (h: FoundHandler) : EntryPoint option =
    let set = ctx.Program.Set
    let point = firstPoint set h.Handler

    let projectId =
        match set.Origin h.Handler.Type.Assembly with
        | User pid -> pid
        | _ -> ctx.Project.ProjectId

    match ctx.Program.Attributor.Attribute(projectId, h.Handler, point) with
    | None -> None
    | Some unit ->
        let body = h.Caller.Body.Value.Value
        let sources = ctx.Taint.Sources |> List.truncate 3

        let steps =
            [ for s in sources -> step $"request data enters through {s.Rule.Id} in {s.Method.Display}" s.Method s.Point
              for g in h.Guards -> step $"{h.Caller.Ref.Display} branches on request data" h.Caller.Ref (Instruction.pointAt body.Instructions g)
              yield step $"{h.Caller.Ref.Display} calls the handler {h.Handler.Display}" h.Caller.Ref (Instruction.pointAt body.Instructions h.CallIndex) ]

        Some
            { Id = ""
              ApplicationId = ctx.Project.ProjectId
              HandlerUnit = unit.Id
              HandlerMember = h.Handler.Display
              Location = point |> Option.map Attribution.toLocation
              Category = categoryOf h.Tags
              RegisteredThrough = (if categoryOf h.Tags = EffectCategory.Network then sources |> List.tryPick (fun s -> if s.Rule.Category = Some EffectCategory.Network then Some s.Rule.Type else None) else None)
              Predicates = []
              HttpMethod = None
              Route = None
              Completeness = Completeness.Unresolved
              Forms = []
              Evidence = { Steps = steps } }

/// Entry points registered with the platform's routing (form C).
let platformEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.choose (fun r ->
        let handlerSpec = r.Rule.Payload |> List.tryFind (fun p -> p.Role = "handler")
        let templateSpec = r.Rule.Payload |> List.tryFind (fun p -> p.Role = "route-template")

        match handlerSpec, templateSpec with
        | Some hs, Some ts ->
            let operands = r.Flow.OperandsAt r.Offset
            let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)
            let handlerOperand = List.tryItem (Values.operandIndex r.Declared false hs.Argument) operands

            match handlerOperand |> Option.bind (PlatformRouting.handlerOf set r.Flow) with
            | None -> None
            | Some target ->
                let handler = PlatformRouting.unwrap set isUser 0 target
                let point = firstPoint set handler
                let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

                let projectId =
                    match set.Origin handler.Type.Assembly with
                    | User pid -> pid
                    | _ -> ctx.Project.ProjectId

                match ctx.Program.Attributor.Attribute(projectId, handler, point) with
                | None -> None
                | Some unit ->
                    let template = evaluator.Operand(r.Offset, Values.operandIndex r.Declared false ts.Argument)

                    let prefix =
                        match operands with
                        | builder :: _ -> PlatformRouting.prefixOf ctx.Matcher evaluator r.Flow r.Caller.Ref 0 builder
                        | [] -> Some []

                    let httpMethod =
                        match r.Rule.HttpMethod with
                        | Some m -> Some m
                        | None ->
                            r.Rule.Payload
                            |> List.tryFind (fun p -> p.Role = "http-methods")
                            |> Option.bind (fun spec ->
                                match evaluator.Operand(r.Offset, Values.operandIndex r.Declared false spec.Argument) with
                                | Values.Text m -> Some m
                                | Values.Parts parts ->
                                    let texts = parts |> List.choose (function Values.Text t -> Some t | _ -> None)
                                    if texts.Length = parts.Length && not texts.IsEmpty then Some(String.concat "," texts) else None
                                | _ -> None)

                    let location = registrationPoint |> Option.map Attribution.toLocation

                    let route, predicates, completeness =
                        match template, prefix with
                        | Values.Text t, Some p ->
                            // Method and full path known: complete. A template without a method (Map): partial.
                            let route = PlatformRouting.normalizeRoute (p @ [ t ])
                            Some route, PlatformRouting.templatePredicates route location, (if httpMethod.IsSome then Completeness.Complete else Completeness.Partial)
                        | _ -> None, [], (if httpMethod.IsSome then Completeness.Partial else Completeness.Unresolved)

                    let methodPredicate =
                        httpMethod
                        |> Option.map (fun m ->
                            { Part = RequestPart.Method
                              Operator = PredicateOperator.Equals
                              Index = None
                              Value = m
                              Location = location })
                        |> Option.toList

                    Some
                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = handler.Display
                          Location = point |> Option.map Attribution.toLocation
                          Category = EffectCategory.HttpIn
                          RegisteredThrough = None
                          Predicates = methodPredicate @ predicates
                          HttpMethod = httpMethod
                          Route = route
                          Completeness = completeness
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{r.Caller.Ref.Display} registers {handler.Display} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint ] } }
        | _ -> None)

// ---- MVC controllers (attribute and conventional routing) ----

/// The catalog rules for an attribute, matched as a call of its constructor.
let private attributeRules (matcher: Matcher) (a: AttributeDef) =
    let ctor =
        { Type = a.Type
          Name = ".ctor"
          Key = $"[attribute]{a.Type.Assembly}:{a.Type.FullName}::.ctor"
          LocalKey = ""
          ParameterTypes = []
          ReturnType = "System.Void"
          HasThis = true
          GenericArity = 0
          TypeArguments = [] }

    matcher.Match ctor |> List.filter (fun r -> r.Kind = RuleKind.Metadata)

let private hasRole (role: string) (rule: Rule) = rule.Payload |> List.exists (fun p -> p.Role = role)

let private textArgument (rule: Rule) (role: string) (a: AttributeDef) : string option =
    rule.Payload
    |> List.tryFind (fun p -> p.Role = role)
    |> Option.bind (fun p -> List.tryItem p.Argument a.Arguments)
    |> Option.bind (function AttributeValue.Text t -> Some t | _ -> None)

/// Fills in the [controller], [action] and [area] tokens of an attribute route.
let private replaceTokens (controller: string) (action: string) (area: string option) (template: string) =
    let t = template.Replace("[controller]", controller).Replace("[action]", action)

    match area with
    | Some a -> t.Replace("[area]", a)
    | None -> t

/// Joins a controller's route with an action's, as MVC does: an action route starting with "/" or "~/" stands alone.
let combineRoutes (controllerRoute: string option) (actionRoute: string option) : string option =
    match controllerRoute, actionRoute with
    | _, Some a when a.StartsWith "/" || a.StartsWith "~/" -> Some(PlatformRouting.normalizeRoute [ a.TrimStart('~') ])
    | Some c, Some a -> Some(PlatformRouting.normalizeRoute [ c; a ])
    | Some c, None -> Some(PlatformRouting.normalizeRoute [ c ])
    | None, Some a -> Some(PlatformRouting.normalizeRoute [ a ])
    | None, None -> None

/// A segment of a conventional route pattern: literal text, or a parameter with an optional default
/// (`{action=Index}`), optional mark (`{id?}`) and constraints (`{id:int}`).
type PatternSegment =
    | Literal of string
    | Parameter of name: string * defaultValue: string option * optional: bool * text: string

let parsePattern (pattern: string) : PatternSegment list =
    pattern.Trim('/').Split('/', System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.map (fun s ->
        if s.StartsWith "{" && s.EndsWith "}" && s.IndexOf('{', 1) < 0 then
            let inner = s.Substring(1, s.Length - 2)
            let optional = inner.EndsWith "?"
            let inner = inner.TrimEnd '?'

            let head, defaultValue =
                match inner.IndexOf '=' with
                | -1 -> inner, None
                | i -> inner.Substring(0, i), Some(inner.Substring(i + 1))

            let name =
                (match head.IndexOf ':' with
                 | -1 -> head
                 | i -> head.Substring(0, i))
                    .TrimStart('*')

            Parameter(name.ToLowerInvariant(), defaultValue, optional, s)
        else
            Literal s)

/// The routes a conventional pattern gives an action: the pattern with the controller, action and area filled in,
/// and the shorter forms its defaults allow (`{controller=Home}/{action=Index}/{id?}` gives Home.Index `/Home/Index/{id?}`,
/// `/Home` and `/`). `defaults` are the controller, action and area values of the route's defaults object (lower-case
/// keys): they act as the defaults of those parameters, and where the pattern has no such parameter they name the only
/// value the route accepts (`blog/{*article}` with controller Blog and action Article reaches only Blog.Article). Empty
/// when the pattern does not reach the action: no controller or action, or an area that does not match the controller's.
let conventionalRoutes (pattern: string) (requiredArea: string option) (defaults: Map<string, string>) (controller: string) (action: string) (area: string option) =
    let same (a: string) (b: string) = System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    let segments =
        parsePattern pattern
        |> List.map (function
            | Parameter(n, None, optional, t) when defaults.ContainsKey n -> Parameter(n, Some defaults[n], optional, t)
            | s -> s)

    let has name = segments |> List.exists (function Parameter(n, _, _, _) -> n = name | _ -> false)

    // A value the pattern does not take as a parameter must be the one its defaults name.
    let fixedMatches name (value: string) =
        has name || defaults.TryFind name |> Option.exists (same value)

    let areaMatches =
        if has "area" then area.IsSome
        else
            match requiredArea |> Option.orElse (defaults.TryFind "area") with
            | Some a -> area |> Option.exists (same a)
            | None -> area.IsNone

    if not (fixedMatches "controller" controller && fixedMatches "action" action) || not areaMatches then
        []
    else
        let values =
            Map.ofList [ yield "controller", controller
                         yield "action", action
                         match area with
                         | Some a -> yield "area", a
                         | None -> () ]

        let text (s: PatternSegment) =
            match s with
            | Literal t -> t
            | Parameter(n, _, _, t) ->
                match values.TryFind n with
                | Some v -> v
                | None -> t

        // A trailing segment can be left out when it is optional, or has a default equal to the value it takes.
        let defaulted (s: PatternSegment) =
            match s with
            | Parameter(n, Some d, _, _) -> values.TryFind n |> Option.exists (same d)
            | _ -> false

        let droppable (s: PatternSegment) =
            match s with
            | Parameter(_, _, true, _) -> true
            | s -> defaulted s

        let n = segments.Length

        let shorter =
            [ for k in n - 1 .. -1 .. 0 do
                  let dropped = segments |> List.skip k
                  if dropped |> List.forall droppable && dropped |> List.exists defaulted then
                      yield segments |> List.truncate k ]

        (segments :: shorter)
        |> List.map (fun ss -> PlatformRouting.normalizeRoute [ "/" + (ss |> List.map text |> String.concat "/") ])
        |> List.distinct

/// Entry points of the controllers an application maps: one per action and route, with the methods its attributes
/// name. Controllers are found the way MVC finds them: public, concrete, non-generic classes named "...Controller"
/// or carrying [Controller] (ControllerBase does, for its subclasses), without [NonController]; their public
/// instance methods are actions unless marked [NonAction]. Actions with attribute routes (MapControllers) are
/// reached by those; the others by the conventional routes the application maps (MapControllerRoute,
/// MapDefaultControllerRoute, MapAreaControllerRoute).
let controllerEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set

    let mapping =
        PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
        |> List.filter (fun r -> hasRole "controllers" r.Rule || hasRole "conventional-controllers" r.Rule)

    if mapping.IsEmpty then
        []
    else
        // Any controller mapping maps the attribute routes: MapControllerRoute and the other conventional mappings
        // create the same endpoint data source as MapControllers, which has an endpoint for every attribute routed
        // action. MapControllers is preferred as the evidence when there is one.
        let mapControllers = mapping |> List.tryFind (fun r -> hasRole "controllers" r.Rule)
        let attributeMapping = mapControllers |> Option.orElse (List.tryHead mapping)
        let pointOf (r: PlatformRouting.Registration) = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

        let operandText (r: PlatformRouting.Registration) (role: string) =
            r.Rule.Payload
            |> List.tryFind (fun p -> p.Role = role)
            |> Option.bind (fun spec ->
                let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)

                match evaluator.Operand(r.Offset, Values.operandIndex r.Declared false spec.Argument) with
                | Values.Text t -> Some t
                | _ -> None)

        // The controller, action and area values of a route's defaults object: an anonymous object or record
        // (`new { controller = "Blog", action = "Article" }`) whose constructor takes them as constants, or null.
        // None when the object is something else, or one of those values is not a constant.
        let routeDefaults (r: PlatformRouting.Registration) : Map<string, string> option =
            match r.Rule.Payload |> List.tryFind (fun p -> p.Role = "route-defaults") with
            | None -> Some Map.empty
            | Some spec ->
                match List.tryItem (Values.operandIndex r.Declared false spec.Argument) (r.Flow.OperandsAt r.Offset) |> Option.map Set.toList with
                | None -> Some Map.empty
                | Some [ Pushed o ] ->
                    match r.Flow.InstructionAt o with
                    | Some { Op = LdNull } -> Some Map.empty
                    | Some { Op = NewObj ctor } when ctor.Type.Name.Contains "AnonymousType" ->
                        match set.TryMethod ctor with
                        | Some(def, _) when def.ParameterNames.Length = ctor.ParameterCount ->
                            let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)

                            let routing =
                                List.zip def.ParameterNames (r.Flow.OperandsAt o)
                                |> List.map (fun (n, v) -> n.ToLowerInvariant(), v)
                                |> List.filter (fun (n, _) -> n = "controller" || n = "action" || n = "area")
                                |> List.map (fun (n, v) ->
                                    match evaluator.Value v with
                                    | Values.Text t -> Some(n, t)
                                    | _ -> None)

                            if routing |> List.forall Option.isSome then Some(routing |> List.choose id |> Map.ofList) else None
                        | _ -> None
                    | _ -> None
                | Some _ -> None

        // Each conventional route: its pattern (None when not a constant), the area it requires, its defaults (None
        // when they cannot be read), and where it is mapped.
        let conventional =
            mapping
            |> List.filter (fun r -> hasRole "conventional-controllers" r.Rule)
            |> List.map (fun r ->
                let pattern =
                    match operandText r "route-pattern" with
                    | Some p -> Some p
                    | None -> if hasRole "route-pattern" r.Rule then None else r.Rule.ComparesTo

                pattern, operandText r "area-name", routeDefaults r, r)

        // The application's own assembly and the user assemblies it references: MVC's application parts.
        let users = ctx.Program.Users |> List.map snd
        let byName = users |> List.map (fun a -> a.Name, a) |> Map.ofList

        let appAssembly =
            ctx.Program.Users |> List.tryFind (fun (p, _) -> p.ProjectId = ctx.Project.ProjectId) |> Option.map snd

        let rec closure (seen: Set<string>) (pending: string list) =
            match pending with
            | [] -> seen
            | n :: rest when seen.Contains n -> closure seen rest
            | n :: rest ->
                match byName.TryFind n with
                | Some a -> closure (seen.Add n) (rest @ a.ReferencedAssemblies)
                | None -> closure seen rest

        let parts =
            match appAssembly with
            | Some a -> closure Set.empty [ a.Name ] |> Set.toList |> List.choose byName.TryFind
            | None -> []

        // Attributes of a type and of its base types, nearest first.
        let rec inheritedAttributes (t: TypeRef) (depth: int) : AttributeDef list =
            if depth > 10 then
                []
            else
                match set.TryType t with
                | Some(d, _) -> d.Attributes @ (match d.BaseType with Some b -> inheritedAttributes b (depth + 1) | None -> [])
                | None -> []

        let attributesWith (role: string) (attributes: AttributeDef list) =
            attributes |> List.filter (fun a -> attributeRules ctx.Matcher a |> List.exists (hasRole role))

        let isController (t: TypeDef) =
            t.IsPublic
            && not t.IsAbstract
            && not t.IsInterface
            && not t.IsValueType
            && not t.IsGeneric
            && not t.IsCompilerGenerated
            && (attributesWith "not-a-controller" (inheritedAttributes t.Ref 0)).IsEmpty
            && (t.Ref.Name.EndsWith "Controller" || not (attributesWith "controller" (inheritedAttributes t.Ref 0)).IsEmpty)

        let routeTemplates (attributes: AttributeDef list) =
            attributes
            |> List.collect (fun a ->
                attributeRules ctx.Matcher a
                |> List.filter (fun r -> r.HttpMethod.IsNone && hasRole "route-template" r)
                |> List.map (fun r -> textArgument r "route-template" a))

        // (methods, template) for each HTTP method attribute of an action.
        let verbs (attributes: AttributeDef list) =
            attributes
            |> List.collect (fun a ->
                attributeRules ctx.Matcher a
                |> List.choose (fun r ->
                    match r.HttpMethod with
                    | Some m -> Some([ m ], textArgument r "route-template" a)
                    | None when hasRole "http-methods" r ->
                        let spec = r.Payload |> List.find (fun p -> p.Role = "http-methods")

                        let methods =
                            match List.tryItem spec.Argument a.Arguments with
                            | Some(AttributeValue.Many items) -> items |> List.choose (function AttributeValue.Text t -> Some(t.ToUpperInvariant()) | _ -> None)
                            | Some(AttributeValue.Text t) -> [ t.ToUpperInvariant() ]
                            | _ -> []

                        let template =
                            a.Named |> List.tryPick (fun (n, v) -> match n, v with "Route", AttributeValue.Text t -> Some t | _ -> None)

                        Some(methods, template)
                    | None -> None))

        // ---- Application model conventions ----
        // A convention's Apply runs over the model MVC builds and can rewrite routes, names and methods. The
        // analysis does not evaluate it: it finds the conventions, reads their code for the model members that
        // change routes (catalog role "route-change"), and marks the routes they may change partial.

        // The Apply methods of a type that is a convention, its own and inherited.
        let appliesCache = Dictionary<string, MethodDef list>()

        let conventionApplies (t: TypeRef) : MethodDef list =
            match appliesCache.TryGetValue t.FullName with
            | true, ms -> ms
            | _ ->
                let ms =
                    set.BaseChain t
                    |> List.collect (fun b -> match set.TryType b with Some(d, _) -> d.Methods | None -> [])
                    |> List.filter (fun m ->
                        (m.Ref.Name = "Apply" || m.Ref.Name.EndsWith ".Apply")
                        && ctx.Matcher.Match m.Ref |> List.exists (hasRole "model-convention"))

                appliesCache[t.FullName] <- ms
                ms

        let routeChangeCache = Dictionary<string, string option>()

        // How a convention changes routes: the first route-changing member its Apply reaches through user and
        // dependency code (closures included), or None when it reaches none. A platform convention, or code the
        // analysis cannot read, is taken to change them.
        let routeChange (t: TypeRef) : string option =
            match routeChangeCache.TryGetValue t.FullName with
            | true, c -> c
            | _ ->
                let seen = HashSet<string>()

                let rec scan (depth: int) (m: MethodDef) : string option =
                    if depth > 8 || not (seen.Add m.Ref.Key) then
                        None
                    else
                        match m.Body.Value with
                        | None -> Some $"{m.Ref.Display}, whose code the analysis cannot read"
                        | Some body ->
                            body.Instructions
                            |> Array.tryPick (fun i ->
                                match i.Op with
                                | Call c
                                | CallVirt c
                                | NewObj c
                                | LdFtn c
                                | LdVirtFtn c ->
                                    if ctx.Matcher.Match c |> List.exists (hasRole "route-change") then
                                        Some $"it uses {c.Display}"
                                    elif set.Origin c.Type.Assembly = Framework then
                                        None
                                    else
                                        let callee = match set.TryMethod c with Some(d, _) -> [ d ] | None -> []

                                        // A closure created here runs through members the code does not name.
                                        let closure =
                                            match i.Op, set.TryType c.Type with
                                            | NewObj _, Some(d, _) when d.IsCompilerGenerated || d.Ref.FullName.Contains "@" -> d.Methods
                                            | _ -> []

                                        (callee @ closure) |> List.tryPick (scan (depth + 1))
                                | _ -> None)

                let change =
                    if set.Origin t.Assembly = Framework then Some "it is a platform convention"
                    else conventionApplies t |> List.tryPick (scan 0)

                routeChangeCache[t.FullName] <- change
                change

        // Conventions the application creates (to add to MvcOptions.Conventions), that change routes: every
        // controller and action is subject to them.
        let applicationConventions =
            ctx.Methods.Values
            |> Seq.collect (fun m ->
                match m.Body.Value with
                | None -> Seq.empty
                | Some body ->
                    body.Instructions
                    |> Seq.mapi (fun i ins -> i, ins)
                    |> Seq.choose (fun (i, ins) ->
                        match ins.Op with
                        | NewObj c when not (conventionApplies c.Type).IsEmpty ->
                            routeChange c.Type
                            |> Option.map (fun how ->
                                c.Type.FullName,
                                step
                                    $"{m.Ref.Display} creates the application model convention {c.Type.FullName}, which may change routes ({how}); the analysis does not follow it"
                                    m.Ref
                                    (Instruction.pointAt body.Instructions i))
                        | _ -> None))
            |> Seq.distinctBy fst
            |> Seq.map snd
            |> Seq.toList

        // Attributes that are conventions changing routes, on a controller or an action.
        let attributeConventions (target: string) (attributes: AttributeDef list) (m: MethodRef) (point: SourcePoint option) =
            attributes
            |> List.filter (fun a -> not (conventionApplies a.Type).IsEmpty)
            |> List.choose (fun a ->
                routeChange a.Type
                |> Option.map (fun how ->
                    step $"{a.Type.FullName} on {target} is an application model convention, which may change its routes ({how}); the analysis does not follow it" m point))

        parts
        |> List.collect (fun assembly -> assembly.Types)
        |> List.filter isController
        |> List.collect (fun controller ->
            let controllerName =
                let n = controller.Ref.Name
                if n.EndsWith "Controller" && n.Length > "Controller".Length then n.Substring(0, n.Length - "Controller".Length) else n

            let inherited = inheritedAttributes controller.Ref 0

            // [Route] is inherited from base controllers; [Area] is taken from the nearest type that has one.
            let controllerRoutes = routeTemplates inherited

            let area =
                attributesWith "area" inherited
                |> List.tryPick (fun a -> attributeRules ctx.Matcher a |> List.tryPick (fun r -> textArgument r "area" a))

            // The controller's methods and those it inherits from base classes in user code, nearest first; a
            // method the controller overrides or hides is its own.
            let rec userBases (t: TypeRef option) (depth: int) =
                match t with
                | Some b when depth < 10 ->
                    match set.Origin b.Assembly, set.TryType b with
                    | User _, Some(d, _) -> d :: userBases d.BaseType (depth + 1)
                    | _ -> []
                | _ -> []

            let signature (m: MethodDef) = m.Ref.Name, m.Ref.ParameterTypes

            let methods =
                (controller :: userBases controller.BaseType 0)
                |> List.collect (fun t -> t.Methods |> List.map (fun m -> t, m))
                |> List.distinctBy (snd >> signature)

            // MVC reads [NonAction] with inheritance, so an override of a [NonAction] method (Controller's
            // OnActionExecuting) is not an action either; nor is an override of an object method, or Dispose.
            let overridesNonAction (owner: TypeDef) (m: MethodDef) =
                let rec walk (t: TypeRef option) (depth: int) =
                    match t |> Option.bind set.TryType with
                    | Some(b, _) when depth < 10 ->
                        match b.Methods |> List.tryFind (fun bm -> bm.IsVirtual && signature bm = signature m) with
                        | Some bm ->
                            b.Ref.FullName = "System.Object"
                            || not (attributesWith "not-an-action" bm.Attributes).IsEmpty
                            || walk b.BaseType (depth + 1)
                        | None -> walk b.BaseType (depth + 1)
                    | _ -> false

                m.IsVirtual && walk owner.BaseType 0

            methods
            |> List.filter (fun (owner, m) ->
                m.IsPublic
                && not m.IsStatic
                && not m.IsConstructor
                && not m.IsAbstract
                && not m.IsSpecialName
                && not m.IsCompilerGenerated
                && (attributesWith "not-an-action" m.Attributes).IsEmpty
                && not (m.Ref.Name = "Dispose" && m.Ref.ParameterTypes.IsEmpty)
                && not (overridesNonAction owner m))
            |> List.map snd
            |> List.collect (fun action ->
                // [ActionName] renames the action for routing and the [action] token.
                let actionName =
                    attributesWith "action-name" action.Attributes
                    |> List.tryPick (fun a -> attributeRules ctx.Matcher a |> List.tryPick (fun r -> textArgument r "action-name" a))
                    |> Option.defaultValue action.Ref.Name
                let actionRoutes = routeTemplates action.Attributes
                let actionVerbs = verbs action.Attributes
                let fill = replaceTokens controllerName actionName area

                // An action is attribute routed when it or its controller has a route template.
                let routed = not controllerRoutes.IsEmpty || not actionRoutes.IsEmpty || actionVerbs |> List.exists (fun (_, t) -> t.IsSome)

                // Methods without a template still restrict a conventionally routed action; none means any method.
                let methodsOf (ms: string list) =
                    match ms with
                    | [] -> [ None ]
                    | ms -> ms |> List.map Some

                // (method, route, how it was routed, the registration that maps it)
                let routes =
                    if routed then
                        // Every (methods, action template) the attributes declare; no verb attribute means any method.
                        let declared =
                            match actionVerbs with
                            | [] -> (if actionRoutes.IsEmpty then [ None ] else actionRoutes) |> List.map (fun t -> [], t)
                            | vs ->
                                vs
                                |> List.collect (fun (ms, t) ->
                                    match t with
                                    | Some _ -> [ ms, t ]
                                    | None -> (if actionRoutes.IsEmpty then [ None ] else actionRoutes) |> List.map (fun r -> ms, r))

                        declared
                        |> List.collect (fun (methods, actionTemplate) ->
                            (if controllerRoutes.IsEmpty then [ None ] else controllerRoutes)
                            |> List.map (fun c -> methods, combineRoutes (c |> Option.map fill) (actionTemplate |> Option.map fill)))
                        |> List.distinct
                        |> List.collect (fun (methods, route) ->
                            methodsOf methods |> List.map (fun m -> m, route, "its routing attributes", attributeMapping.Value))
                    else
                        let methods = actionVerbs |> List.collect fst |> List.distinct

                        let byConvention =
                            conventional
                            |> List.collect (fun (pattern, requiredArea, defaults, r) ->
                                match pattern, defaults with
                                | Some p, Some d ->
                                    conventionalRoutes p requiredArea d controllerName actionName area
                                    |> List.map (fun route -> Some route, $"the conventional route \"{p}\"", r)
                                | Some p, None ->
                                    // Without its defaults, a pattern that names the controller and action still
                                    // gives the full route; one that does not could reach any action.
                                    let names = parsePattern p |> List.choose (function Parameter(n, _, _, _) -> Some n | _ -> None)

                                    if List.contains "controller" names && List.contains "action" names then
                                        conventionalRoutes p requiredArea Map.empty controllerName actionName area
                                        |> List.truncate 1
                                        |> List.map (fun route -> Some route, $"the conventional route \"{p}\"", r)
                                    else
                                        [ None, $"the conventional route \"{p}\", whose defaults are not constants", r ]
                                | None, _ -> [ None, "a conventional route whose pattern is not a constant", r ])
                            |> List.distinctBy (fun (route, _, _) -> route)

                        match byConvention, mapControllers with
                        | [], Some r -> methodsOf methods |> List.map (fun m -> m, None, "no route: it has no routing attributes and no conventional route reaches it", r)
                        | [], None -> []
                        | rs, _ -> rs |> List.collect (fun (route, how, r) -> methodsOf methods |> List.map (fun m -> m, route, how, r))

                let point = firstPoint set action.Ref

                let projectId =
                    match set.Origin action.Ref.Type.Assembly with
                    | User pid -> pid
                    | _ -> ctx.Project.ProjectId

                match ctx.Program.Attributor.Attribute(projectId, action.Ref, point) with
                | None -> []
                | Some unit ->
                    let location = point |> Option.map Attribution.toLocation

                    let conventions =
                        applicationConventions
                        @ attributeConventions $"controller {controller.Ref.FullName}" inherited action.Ref point
                        @ attributeConventions $"action {action.Ref.Display}" action.Attributes action.Ref point

                    routes
                    |> List.map (fun (httpMethod, route, how, registration) ->
                        let methodPredicate =
                            httpMethod
                            |> Option.map (fun m ->
                                { Part = RequestPart.Method
                                  Operator = PredicateOperator.Equals
                                  Index = None
                                  Value = m
                                  Location = location })
                            |> Option.toList

                        let routePredicates = route |> Option.map (fun r -> PlatformRouting.templatePredicates r location) |> Option.defaultValue []

                        // A convention that may change the route leaves it partial at best.
                        let completeness =
                            match httpMethod, route with
                            | Some _, Some _ when conventions.IsEmpty -> Completeness.Complete
                            | None, None -> Completeness.Unresolved
                            | _ -> Completeness.Partial

                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = action.Ref.Display
                          Location = location
                          Category = EffectCategory.HttpIn
                          RegisteredThrough = None
                          Predicates = methodPredicate @ routePredicates
                          HttpMethod = httpMethod
                          Route = route
                          Completeness = completeness
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{registration.Caller.Ref.Display} maps controllers with {registration.Declared.Display} (rule {registration.Rule.Id})" registration.Caller.Ref (pointOf registration)
                                  step $"{action.Ref.Display} is an action of controller {controller.Ref.FullName}, routed by {how}" action.Ref point ]
                                @ conventions } })))

// ---- SignalR hubs ----

/// Hubs mapped by a registration whose rule has a "hub-type" payload (MapHub<THub>(pattern)): the type argument
/// at that index is the hub, and its clients invoke its public methods by name over the connection the pattern
/// accepts. One entry point per hub method, at the pattern: the HTTP method does not choose the handler, the
/// invocation's target does, so the route is partial.
let hubEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set

    // The hub's methods clients can invoke: its public instance methods, and those of its base classes outside the
    // shared framework, except constructors, accessors, generic methods and overrides of the framework's
    // virtual methods (OnConnectedAsync, OnDisconnectedAsync, Dispose).
    let hubMethods (hub: TypeRef) =
        let chain = hub :: set.BaseChain hub
        let user = chain |> List.filter (fun t -> set.Origin t.Assembly <> Framework)

        let frameworkVirtuals =
            chain
            |> List.filter (fun t -> set.Origin t.Assembly = Framework)
            |> List.choose set.TryType
            |> List.collect (fun (t, _) -> t.Methods |> List.filter (fun m -> m.IsVirtual) |> List.map (fun m -> m.Ref.Name))
            |> Set.ofList

        user
        |> List.choose set.TryType
        |> List.collect (fun (t, _) ->
            t.Methods
            |> List.filter (fun m ->
                m.IsPublic
                && not m.IsStatic
                && not m.IsConstructor
                && not m.IsSpecialName
                && m.Ref.GenericArity = 0
                && not (m.IsVirtual && frameworkVirtuals.Contains m.Ref.Name)))
        |> List.distinctBy (fun m -> m.Ref.Name, m.Ref.ParameterTypes)

    // The name a client invokes a method by: [HubMethodName("...")] when the catalog names that attribute.
    let invokedAs (m: MethodDef) =
        m.Attributes
        |> List.tryPick (fun a -> attributeRules ctx.Matcher a |> List.tryPick (fun r -> textArgument r "hub-method-name" a))
        |> Option.defaultValue m.Ref.Name

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.filter (fun r -> hasRole "hub-type" r.Rule)
    |> List.collect (fun r ->
        let hubSpec = r.Rule.Payload |> List.find (fun p -> p.Role = "hub-type")

        match List.tryItem hubSpec.Argument r.Declared.TypeArguments with
        | None -> []
        | Some hub ->
            let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)
            let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index
            let location = registrationPoint |> Option.map Attribution.toLocation

            let route =
                let template =
                    r.Rule.Payload
                    |> List.tryFind (fun p -> p.Role = "route-template")
                    |> Option.map (fun ts -> evaluator.Operand(r.Offset, Values.operandIndex r.Declared false ts.Argument))

                let prefix =
                    match r.Flow.OperandsAt r.Offset with
                    | builder :: _ -> PlatformRouting.prefixOf ctx.Matcher evaluator r.Flow r.Caller.Ref 0 builder
                    | [] -> Some []

                match template, prefix with
                | Some(Values.Text t), Some p -> Some(PlatformRouting.normalizeRoute (p @ [ t ]))
                | _ -> None

            hubMethods hub
            |> List.choose (fun m ->
                let handler = m.Ref
                let point = firstPoint set handler

                let projectId =
                    match set.Origin handler.Type.Assembly with
                    | User pid -> pid
                    | _ -> ctx.Project.ProjectId

                ctx.Program.Attributor.Attribute(projectId, handler, point)
                |> Option.map (fun unit ->
                    let name = invokedAs m

                    { Id = ""
                      ApplicationId = ctx.Project.ProjectId
                      HandlerUnit = unit.Id
                      HandlerMember = handler.Display
                      Location = point |> Option.map Attribution.toLocation
                      Category = EffectCategory.HttpIn
                      RegisteredThrough = None
                      Predicates =
                        match route with
                        | Some route -> PlatformRouting.templatePredicates route location
                        | None -> []
                      HttpMethod = None
                      Route = route
                      Completeness = if route.IsSome then Completeness.Partial else Completeness.Unresolved
                      Forms = if route.IsSome then [ RouteForm.PlatformRouting ] else []
                      Evidence =
                        { Steps =
                            [ step $"{r.Caller.Ref.Display} maps hub {hub.FullName} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                              step $"{handler.Display} is a method of the hub, which clients invoke as \"{name}\" over its connection" handler point ] } })))

// ---- Hosted services ----

/// Types a host or library calls: a registration whose rule has a "hosted-service-type" payload (AddHostedService<T>())
/// or a "consumer-type" payload (MassTransit's AddConsumer<T>()) names the type by its type argument, and the
/// callback rules of the registration's category on the type's base types name the members called: the host's
/// IHostedService.StartAsync and a BackgroundService's ExecuteAsync (background), a bus's IConsumer<T>.Consume
/// (network). Each such member the application implements is an entry point; a consumer's is at the message type
/// its parameter is a context of.
let backgroundEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set

    // The message type of a consumer's method: the type argument of its first parameter (ConsumeContext<T>).
    let messageType (m: MethodRef) =
        match m.ParameterTypes with
        | first :: _ when first.Contains '<' && first.EndsWith ">" ->
            let inner = first.Substring(first.IndexOf '<' + 1).TrimEnd('>')
            if inner.Contains ',' || inner.Contains '<' || inner.StartsWith "!" then None else Some inner
        | _ -> None

    // The roles that name a type by its type argument, what the type is, and who calls it.
    let roles =
        [ "hosted-service-type", ("hosted service", "The host")
          "consumer-type", ("consumer", "The bus")
          "job-type", ("job", "The scheduler") ]
        |> dict

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.filter (fun r -> r.Rule.Payload |> List.exists (fun p -> roles.ContainsKey p.Role))
    |> List.collect (fun r ->
        let spec = r.Rule.Payload |> List.find (fun p -> roles.ContainsKey p.Role)
        let what, who = roles[spec.Role]
        let isConsumer = spec.Role = "consumer-type"
        let category = defaultArg r.Rule.Category EffectCategory.Background
        let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

        match List.tryItem spec.Argument r.Declared.TypeArguments with
        | None -> []
        | Some service ->
            ctx.Matcher.Callbacks service
            |> List.filter (fun rule -> rule.Category = Some category)
            |> List.collect (fun rule ->
                match set.TryType { Assembly = rule.Assembly; FullName = rule.Type } with
                | None -> []
                | Some(declaring, _) ->
                    declaring.Methods
                    |> List.filter (fun d -> rule.Members |> List.exists (fun p -> Matcher.MemberMatches p d.Ref.Name))
                    |> List.choose (fun d -> set.Implementation(service, d.Ref) |> Option.map (fun impl -> rule, d, impl)))
            |> List.filter (fun (_, _, impl) ->
                match set.Origin impl.Type.Assembly with
                | User _ -> true
                | _ -> false)
            |> List.distinctBy (fun (_, _, impl) -> impl.Key)
            |> List.choose (fun (rule, declared, handler) ->
                let point = firstPoint set handler

                let projectId =
                    match set.Origin handler.Type.Assembly with
                    | User pid -> pid
                    | _ -> ctx.Project.ProjectId

                ctx.Program.Attributor.Attribute(projectId, handler, point)
                |> Option.map (fun unit ->
                    let route = if isConsumer then messageType handler else None

                    { Id = ""
                      ApplicationId = ctx.Project.ProjectId
                      HandlerUnit = unit.Id
                      HandlerMember = handler.Display
                      Location = point |> Option.map Attribution.toLocation
                      Category = category
                      RegisteredThrough = Some r.Rule.Type
                      Predicates = []
                      HttpMethod = None
                      Route = route
                      Completeness = if route.IsSome || not isConsumer then Completeness.Complete else Completeness.Unresolved
                      Forms = [ RouteForm.PlatformRouting ]
                      Evidence =
                        { Steps =
                            [ step $"{r.Caller.Ref.Display} registers {what} {service.FullName} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                              step $"{who} calls {declared.Ref.Display}, which {handler.Display} implements (rule {rule.Id})" handler point ] } })))

// ---- gRPC services ----

/// The methods a delegate among the values is made from (`new UnaryServerMethod(impl.SayHello)`).
let private delegateTargets (flow: Flow) (values: Values) : MethodRef list =
    values
    |> Set.toList
    |> List.choose (function
        | Pushed o ->
            match flow.InstructionAt o with
            | Some { Op = NewObj _ } ->
                match flow.OperandsAt o with
                | [ _; fn ] ->
                    fn
                    |> Set.toList
                    |> List.tryPick (function
                        | Pushed f ->
                            match flow.InstructionAt f with
                            | Some { Op = (LdFtn m | LdVirtFtn m) } -> Some m
                            | _ -> None
                        | _ -> None)
                | _ -> None
            | _ -> None
        | _ -> None)

/// gRPC services mapped by a registration whose rule has a "grpc-service-type" payload (MapGrpcService<T>()).
/// The library binds a service by reflection: an attribute on the service's generated base class names the type
/// and the static method that binds it (`[BindServiceMethod(typeof(Greeter), "BindService")]`), and that method, in
/// the application's own assembly, adds each method with a descriptor made in its type's static constructor from
/// the service's and the method's names, and a delegate to the base class's method. Each method the service
/// overrides is an entry point at `POST /{service}/{method}`.
let grpcEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let rules = ctx.Matcher.Catalog.Rules

    let specOf (role: string) =
        rules
        |> List.tryPick (fun r -> r.Payload |> List.tryFind (fun p -> p.Role = role) |> Option.map (fun p -> r, p))

    let generic (name: string) =
        match name.IndexOf '<' with
        | -1 -> name
        | i -> name.Substring(0, i)

    // The static methods that bind a service (overloads for each binder), from the attribute on a class it derives
    // from, and that class.
    let bindMethods (service: TypeRef) =
        match specOf "grpc-bind-type", specOf "grpc-bind-method" with
        | Some(rule, typeSpec), Some(_, methodSpec) ->
            (service :: set.BaseChain service)
            |> List.tryPick (fun t ->
                set.TryType t
                |> Option.bind (fun (def, _) ->
                    def.Attributes
                    |> List.tryFind (fun a -> a.Type.FullName = rule.Type)
                    |> Option.bind (fun a ->
                        match List.tryItem typeSpec.Argument a.Arguments, List.tryItem methodSpec.Argument a.Arguments with
                        | Some(AttributeValue.TypeOf bindType), Some(AttributeValue.Text name) ->
                            set.TryType bindType
                            |> Option.map (fun (bt, _) -> bt.Methods |> List.filter (fun m -> m.IsStatic && m.Ref.Name = name), t)
                        | _ -> None)))
        | _ -> None

    match specOf "grpc-method-descriptor", specOf "grpc-handler", specOf "grpc-service-name", specOf "grpc-method-name" with
    | Some(addRule, descriptorSpec), Some(_, handlerSpec), Some(descriptorRule, serviceNameSpec), Some(_, methodNameSpec) ->
        PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
        |> List.filter (fun r -> hasRole "grpc-service-type" r.Rule)
        |> List.collect (fun r ->
            let spec = r.Rule.Payload |> List.find (fun p -> p.Role = "grpc-service-type")
            let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

            match List.tryItem spec.Argument r.Declared.TypeArguments |> Option.map (fun s -> s, bindMethods s) with
            | Some(service, Some(binds, generatedBase)) ->
                binds
                |> List.collect (fun bind ->
                    match DataFlow.ofMethod bind with
                    | None -> []
                    | Some flow ->
                        flow.Body.Instructions
                        |> Array.toList
                        |> List.choose (fun ins ->
                            match ins.Op with
                            | Call added
                            | CallVirt added when generic added.Type.FullName = addRule.Type && List.contains added.Name addRule.Members ->
                                let operand (s: PayloadSpec) = List.tryItem (Values.operandIndex added false s.Argument) (flow.OperandsAt ins.Offset)

                                // The descriptor: a static field made by the descriptor type's constructor.
                                let names =
                                    operand descriptorSpec
                                    |> Option.bind (fun d ->
                                        Values.staticObjectTexts set flow d descriptorRule.Type [ serviceNameSpec.Argument; methodNameSpec.Argument ])
                                    |> Option.bind (function
                                        | [ s; m ] -> Some(s, m)
                                        | _ -> None)

                                let handlers =
                                    operand handlerSpec
                                    |> Option.map (delegateTargets flow)
                                    |> Option.defaultValue []
                                    |> List.choose (fun m -> set.Implementation(service, m))
                                    // Not overridden: the generated base class answers that the method is unimplemented.
                                    |> List.filter (fun impl -> impl.Type.FullName <> generatedBase.FullName)

                                match names, handlers with
                                | Some(serviceName, methodName), handler :: _ -> Some(serviceName, methodName, handler)
                                | _ -> None
                            | _ -> None)
                        |> List.choose (fun (serviceName, methodName, handler) ->
                            let point = firstPoint set handler

                            let projectId =
                                match set.Origin handler.Type.Assembly with
                                | User pid -> pid
                                | _ -> ctx.Project.ProjectId

                            ctx.Program.Attributor.Attribute(projectId, handler, point)
                            |> Option.map (fun unit ->
                                let route = $"/{serviceName}/{methodName}"
                                let location = registrationPoint |> Option.map Attribution.toLocation

                                { Id = ""
                                  ApplicationId = ctx.Project.ProjectId
                                  HandlerUnit = unit.Id
                                  HandlerMember = handler.Display
                                  Location = point |> Option.map Attribution.toLocation
                                  Category = EffectCategory.HttpIn
                                  RegisteredThrough = None
                                  Predicates =
                                    { Part = RequestPart.Method
                                      Operator = PredicateOperator.Equals
                                      Index = None
                                      Value = "POST"
                                      Location = location }
                                    :: PlatformRouting.templatePredicates route location
                                  HttpMethod = Some "POST"
                                  Route = Some route
                                  Completeness = Completeness.Complete
                                  Forms = [ RouteForm.PlatformRouting ]
                                  Evidence =
                                    { Steps =
                                        [ step $"{r.Caller.Ref.Display} maps gRPC service {service.FullName} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                                          step $"{bind.Ref.Display}, named by an attribute of {generatedBase.FullName}, binds method {methodName} of service {serviceName} (rule {addRule.Id})" bind.Ref None
                                          step $"{handler.Display} implements it" handler point ] } })))
                |> List.distinctBy (fun e -> e.Route, e.HandlerMember)
            | _ -> [])
    | _ -> []

// ---- Message consumers ----

/// Handlers a messaging library calls for each message it receives: a registration whose rule has a
/// "message-handler" payload (RabbitMQ's `consumer.Received += handler`, Azure Service Bus's
/// `processor.ProcessMessageAsync += handler`) adds a delegate to a consumer object. The queue is read from the call
/// that made the consumer, when its rule has a "message-queue" payload (`client.CreateProcessor("orders")`), or
/// from a call in the same method that ties the consumer to a queue, whose rule has "message-queue" and
/// "message-consumer" payloads (`channel.BasicConsume("orders", autoAck, consumer)`). Each handler is an entry
/// point of category network, at the queue.
let messageEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let isUser (m: MethodRef) = ctx.Methods.ContainsKey m.Key

    // Registrations by a call, and by a constructor that takes the handler (a timer's callback).
    let registrations =
        PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
        @ (ctx.Methods.Values
           |> Seq.toList
           |> List.collect (fun m ->
               match m.Body.Value with
               | None -> []
               | Some body ->
                   let flow = lazy (DataFlow.ofMethod m)

                   body.Instructions
                   |> Array.mapi (fun i ins -> i, ins)
                   |> Array.choose (fun (i, ins) ->
                       match ins.Op with
                       | NewObj ctor ->
                           match ctx.Matcher.Registration ctor, flow.Value with
                           | Some rule, Some f when rule.Payload |> List.exists (fun p -> p.Role = "message-handler") ->
                               Some
                                   ({ Caller = m
                                      Offset = ins.Offset
                                      Index = i
                                      Rule = rule
                                      Declared = ctor
                                      Flow = f }
                                   : PlatformRouting.Registration)
                           | _ -> None
                       | _ -> None)
                   |> Array.toList))

    let isNewObj (r: PlatformRouting.Registration) =
        match r.Flow.InstructionAt r.Offset with
        | Some { Op = NewObj _ } -> true
        | _ -> false

    let specOf (rule: Rule) (role: string) = rule.Payload |> List.tryFind (fun p -> p.Role = role)

    let operand (r: PlatformRouting.Registration) (spec: PayloadSpec) =
        List.tryItem (Values.operandIndex r.Declared (isNewObj r) spec.Argument) (r.Flow.OperandsAt r.Offset)

    // A queue named by an argument, or by a property of one (an SQS request's QueueUrl).
    let text (r: PlatformRouting.Registration) (spec: PayloadSpec) =
        let evaluator =
            Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun m n -> ctx.Program.Producers.Member(m, n)), ctx.Program.SettingsFor ctx.Project)

        let index = Values.operandIndex r.Declared (isNewObj r) spec.Argument

        let value =
            match spec.Property with
            | Some property -> evaluator.OperandMember(r.Offset, index, property, spec.ConstructorArgument)
            | None -> evaluator.Operand(r.Offset, index)

        match value with
        | Values.Text t -> Some t
        | _ -> None

    // The method a delegate among the values runs. Roslyn caches a lambda's delegate in a static field and makes it
    // only when the field is empty, so the values are the cached one and the new one: the new one says what runs.
    let handlerIn (flow: Flow) (values: Values) =
        let made, other =
            values
            |> Set.toList
            |> List.partition (fun v ->
                match v with
                | Pushed o -> (match flow.InstructionAt o with Some { Op = NewObj _ } -> true | _ -> false)
                | _ -> false)

        (made @ other) |> List.tryPick (fun v -> PlatformRouting.handlerOf set flow (Set.singleton v))

    // The instruction that made an object, when the values are one object. An async method keeps its locals in
    // fields of its state machine: one read from a field of `this` is what the method stored in it.
    let rec madeIn (flow: Flow) (depth: int) (values: Values) =
        match Set.toList values with
        | [ Pushed o ] ->
            match flow.InstructionAt o, flow.OperandsAt o with
            | Some { Op = LdFld f }, [ receiver ] when receiver.Contains(Argument 0) && depth < 4 ->
                flow.Body.Instructions
                |> Array.tryPick (fun ins ->
                    match ins.Op, flow.OperandsAt ins.Offset with
                    | StFld g, [ target; stored ] when g.Key = f.Key && target.Contains(Argument 0) -> madeIn flow (depth + 1) stored
                    | _ -> None)
            | _ -> Some o
        | _ -> None

    // The roles that name where messages come from, and what to call it: a queue (RabbitMQ, Service Bus), a channel
    // (Redis), a hub method a server invokes (a SignalR client).
    let addressRoles = [ "message-queue", "queue"; "message-channel", "channel"; "message-method", "hub method" ]
    let addressOf (rule: Rule) = addressRoles |> List.tryPick (fun (role, _) -> specOf rule role)

    let nounOf (rule: Rule) =
        addressRoles |> List.tryPick (fun (role, noun) -> specOf rule role |> Option.map (fun _ -> noun)) |> Option.defaultValue "queue"

    // Calls that tie a consumer to a queue, by the method they are in and the instruction that made the consumer:
    // whichever of their arguments is the consumer, as overloads put it in different places.
    let tied =
        registrations
        |> List.collect (fun r ->
            match addressOf r.Rule, specOf r.Rule "message-consumer" with
            | Some queue, Some _ ->
                r.Flow.OperandsAt r.Offset
                |> List.choose (madeIn r.Flow 0)
                |> List.map (fun made -> (r.Caller.Ref.Key, made), (r, text r queue))
            | _ -> [])

    // The method an async method's state machine runs: MoveNext of `T/<Name>d__N` is T's Name.
    let kickoff (m: MethodRef) =
        let t = m.Type.FullName

        match m.Name, t.LastIndexOf "/<" with
        | "MoveNext", i when i > 0 && t.IndexOf('>', i) > i ->
            let name = t.Substring(i + 2, t.IndexOf('>', i) - i - 2)

            set.TryType { m.Type with FullName = t.Substring(0, i) }
            |> Option.bind (fun (owner, _) -> owner.Methods |> List.tryFind (fun d -> d.Ref.Name = name))
            |> Option.map (fun d -> d.Ref)
            |> Option.defaultValue m
        | _ -> m

    // The method an expression tree among the values calls (`() => reports.Daily()` handed to Hangfire): the one
    // whose token the tree's construction loads, as Expression.Call is given it.
    let rec expressionTarget (flow: Flow) (depth: int) (values: Values) : MethodRef option =
        if depth > 16 then
            None
        else
            values
            |> Set.toList
            |> List.tryPick (function
                | Pushed o ->
                    match flow.InstructionAt o with
                    | Some { Op = LdToken(Some m) } when isUser m -> Some m
                    | _ -> flow.OperandsAt o |> List.tryPick (expressionTarget flow (depth + 1))
                | _ -> None)

    // The handler: the delegate a registration adds, the method an expression tree it is given calls, or the method
    // that receives messages itself (a loop that calls Kafka's Consume or Service Bus's ReceiveMessageAsync).
    let handlerOf (r: PlatformRouting.Registration) =
        match specOf r.Rule "message-handler", specOf r.Rule "expression-handler", specOf r.Rule "message-receive" with
        | Some handlerSpec, _, _ ->
            operand r handlerSpec
            |> Option.bind (handlerIn r.Flow)
            |> Option.map (fun target -> PlatformRouting.unwrap set isUser 0 target, false)
        | None, Some _, _ ->
            r.Flow.OperandsAt r.Offset
            |> List.tryPick (expressionTarget r.Flow 0)
            |> Option.map (fun target -> target, false)
        | None, None, Some _ -> Some(kickoff r.Caller.Ref, true)
        | None, None, None -> None

    registrations
    |> List.choose (fun r ->
        match handlerOf r with
        | None -> None
        | Some(handler, receives) ->
            let point = firstPoint set handler
            let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

            // The consumer: the receiver of the registration. A constructor that takes the handler has none.
            let made =
                if isNewObj r then None else r.Flow.OperandsAt r.Offset |> List.tryHead |> Option.bind (madeIn r.Flow 0)

            // Where the queue is named: by the registering or receiving call itself (Redis's Subscribe(channel,
            // handler), RabbitMQ's BasicGet), by the call that made the consumer, or by one that ties it to a queue
            // (a subscription).
            let tiedTo (o: int) = tied |> List.tryFind (fun (key, _) -> key = (r.Caller.Ref.Key, o)) |> Option.map snd

            let own =
                match addressOf r.Rule, specOf r.Rule "message-consumer" with
                | Some queue, None -> Some(r, text r queue)
                | _ -> None

            let source =
                own
                |> Option.orElse (
                    made
                    |> Option.bind (fun o ->
                        match r.Flow.InstructionAt o with
                        | Some { Op = (Call c | CallVirt c) } ->
                            ctx.Matcher.Match c
                            |> List.tryPick (fun rule ->
                                match addressOf rule, specOf rule "message-consumer" with
                                | Some queue, None ->
                                    let made: PlatformRouting.Registration =
                                        { Caller = r.Caller
                                          Offset = o
                                          Index = r.Caller.Body.Value.Value.Instructions |> Array.findIndex (fun i -> i.Offset = o)
                                          Rule = rule
                                          Declared = c
                                          Flow = r.Flow }

                                    Some(made, text made queue)
                                | _ -> None)
                            |> Option.orElse (tiedTo o)
                        | Some { Op = NewObj ctor } ->
                            // Made by a constructor that names it (new FileSystemWatcher(path)), or tied to it later.
                            ctx.Matcher.Match ctor
                            |> List.tryPick (fun rule ->
                                match addressOf rule, specOf rule "message-consumer" with
                                | Some queue, None when rule.Kind = Registration ->
                                    let made: PlatformRouting.Registration =
                                        { Caller = r.Caller
                                          Offset = o
                                          Index = r.Caller.Body.Value.Value.Instructions |> Array.findIndex (fun i -> i.Offset = o)
                                          Rule = rule
                                          Declared = ctor
                                          Flow = r.Flow }

                                    Some(made, text made queue)
                                | _ -> None)
                            |> Option.orElse (tiedTo o)
                        | _ -> None)
                )

            let projectId =
                match set.Origin handler.Type.Assembly with
                | User pid -> pid
                | _ -> ctx.Project.ProjectId

            ctx.Program.Attributor.Attribute(projectId, handler, point)
            |> Option.map (fun unit ->
                let queue = source |> Option.bind snd
                let category = defaultArg r.Rule.Category EffectCategory.Network
                let calledWhen =
                    match category with
                    | EffectCategory.Background -> "each time it fires"
                    | EffectCategory.FilesystemRead -> "when a file it watches changes"
                    | _ -> "for each message received"

                let queueStep =
                    match source with
                    | Some(q, name) ->
                        let noun, verb =
                            match category, nounOf q.Rule with
                            | EffectCategory.FilesystemRead, _ -> "path", "watches"
                            | _, "hub method" -> "hub method", "receives it as"
                            | _, noun -> noun, "reads it from"
                        let named = match name with Some n -> $"{noun} \"{n}\"" | None -> $"a {noun} that is not a constant"
                        [ step $"{q.Caller.Ref.Display} {verb} {named} with {q.Declared.Display} (rule {q.Rule.Id})" q.Caller.Ref (Instruction.pointAt q.Caller.Body.Value.Value.Instructions q.Index) ]
                    | None -> []

                { Id = ""
                  ApplicationId = ctx.Project.ProjectId
                  HandlerUnit = unit.Id
                  HandlerMember = handler.Display
                  Location = point |> Option.map Attribution.toLocation
                  Category = category
                  RegisteredThrough = Some(match source with Some(q, _) -> q.Rule.Type | None -> r.Rule.Type)
                  Predicates = []
                  HttpMethod = None
                  Route = queue
                  // Background work (a timer) has no address; a message comes from a queue.
                  Completeness = if queue.IsSome || category = EffectCategory.Background then Completeness.Complete else Completeness.Unresolved
                  Forms = [ RouteForm.PlatformRouting ]
                  Evidence =
                    { Steps =
                        (if receives then
                             step $"{r.Caller.Ref.Display} receives messages with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                         else
                             step $"{r.Caller.Ref.Display} registers {handler.Display} with {r.Declared.Display}, called {calledWhen} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint)
                        :: queueStep } }))

// ---- Azure Functions ----

/// The route prefix of HTTP-triggered functions: `extensions.http.routePrefix` in the host.json next to the
/// assembly, "api" by default.
let private functionsRoutePrefix (assemblyPath: string) =
    let file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName assemblyPath, "host.json")

    let read () =
        use doc =
            System.Text.Json.JsonDocument.Parse(
                System.IO.File.ReadAllText file,
                System.Text.Json.JsonDocumentOptions(CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)
            )

        let child (e: System.Text.Json.JsonElement) (name: string) =
            if e.ValueKind <> System.Text.Json.JsonValueKind.Object then
                None
            else
                e.EnumerateObject()
                |> Seq.tryFind (fun p -> System.String.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                |> Option.map (fun p -> p.Value)

        child doc.RootElement "extensions"
        |> Option.bind (fun e -> child e "http")
        |> Option.bind (fun e -> child e "routePrefix")
        |> Option.filter (fun e -> e.ValueKind = System.Text.Json.JsonValueKind.String)
        |> Option.map (fun e -> e.GetString())

    try
        if System.IO.File.Exists file then defaultArg (read ()) "api" else "api"
    with
    | :? System.Text.Json.JsonException
    | :? System.IO.IOException -> "api"

/// Functions: methods with an attribute whose metadata rule has a "function-name" payload ([Function("Name")]), run
/// by the host when the trigger on one of their parameters fires: an attribute whose metadata rule has a
/// "function-trigger" payload, of the trigger's category. An HTTP trigger gives the HTTP methods (every string in
/// its arguments) and its "route-template" property (the function's name by default), under host.json's route
/// prefix; any other trigger gives its "trigger-address" argument (a queue, a schedule) as the route.
let functionEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let prefix = lazy (functionsRoutePrefix ctx.Project.AssemblyPath)

    let rulesFor (a: AttributeDef) = attributeRules ctx.Matcher a

    let texts (values: AttributeValue list) =
        values
        |> List.collect (function
            | AttributeValue.Text t -> [ t ]
            | AttributeValue.Many items -> items |> List.choose (function AttributeValue.Text t -> Some t | _ -> None)
            | _ -> [])

    ctx.Methods.Values
    |> Seq.toList
    |> List.choose (fun m ->
        m.Attributes
        |> List.tryPick (fun a -> rulesFor a |> List.tryPick (fun r -> textArgument r "function-name" a))
        |> Option.map (fun name -> m, name))
    |> List.collect (fun (m, functionName) ->
        m.ParameterAttributes.Value
        |> List.concat
        |> List.collect (fun a ->
            rulesFor a
            |> List.filter (hasRole "function-trigger")
            |> List.map (fun r -> a, r))
        |> List.truncate 1
        |> List.choose (fun (trigger, rule) ->
            let handler = m.Ref
            let point = firstPoint set handler
            let location = point |> Option.map Attribution.toLocation

            let projectId =
                match set.Origin handler.Type.Assembly with
                | User pid -> pid
                | _ -> ctx.Project.ProjectId

            ctx.Program.Attributor.Attribute(projectId, handler, point)
            |> Option.map (fun unit ->
                let category = defaultArg rule.Category EffectCategory.HttpIn

                let httpMethod, route, predicates, completeness =
                    if category = EffectCategory.HttpIn then
                        let methods =
                            if hasRole "http-methods" rule then
                                texts trigger.Arguments |> List.map (fun t -> t.ToUpperInvariant()) |> List.distinct
                            else
                                []

                        let template =
                            rule.Payload
                            |> List.tryFind (fun p -> p.Role = "route-template")
                            |> Option.bind (fun p -> p.Property)
                            |> Option.bind (fun property ->
                                trigger.Named |> List.tryPick (fun (n, v) -> match v with AttributeValue.Text t when n = property -> Some t | _ -> None))
                            |> Option.defaultValue functionName

                        let route = PlatformRouting.normalizeRoute ((if prefix.Value = "" then [] else [ prefix.Value ]) @ [ template ])
                        let httpMethod = if methods.IsEmpty then None else Some(String.concat "," methods)

                        let methodPredicate =
                            httpMethod
                            |> Option.map (fun v ->
                                { Part = RequestPart.Method
                                  Operator = PredicateOperator.Equals
                                  Index = None
                                  Value = v
                                  Location = location })
                            |> Option.toList

                        httpMethod,
                        Some route,
                        methodPredicate @ PlatformRouting.templatePredicates route location,
                        (if httpMethod.IsSome then Completeness.Complete else Completeness.Partial)
                    else
                        let address = textArgument rule "trigger-address" trigger
                        None, address, [], (if address.IsSome || not (hasRole "trigger-address" rule) then Completeness.Complete else Completeness.Unresolved)

                { Id = ""
                  ApplicationId = ctx.Project.ProjectId
                  HandlerUnit = unit.Id
                  HandlerMember = handler.Display
                  Location = location
                  Category = category
                  RegisteredThrough = Some rule.Type
                  Predicates = predicates
                  HttpMethod = httpMethod
                  Route = route
                  Completeness = completeness
                  Forms = [ RouteForm.PlatformRouting ]
                  Evidence =
                    { Steps =
                        [ step $"{handler.Display} is function \"{functionName}\", run by the host when its trigger fires: {trigger.Type.FullName} (rule {rule.Id})" handler point ] } })))

// ---- Razor Pages ----

/// Razor Pages mapped by a registration whose rule has a "razor-pages" payload (MapRazorPages). Each page is compiled
/// into a class carrying metadata attributes: the catalog's rule for the metadata attribute names, by its payload's
/// properties, the key whose value is the page's file ("razor-identifier", `/Pages/Orders/Details.cshtml`) and the
/// key whose value is its `@page` template ("razor-route-template"). The route is the file's path under `/Pages`
/// (`/Areas/{area}/Pages` for an area), without `Index`, followed by the template unless it starts with `/`. The
/// page's model (its Model property) handles requests with its `On{Verb}[Name][Async]` methods: one entry point per
/// method, at its HTTP method, a named handler with the query condition `handler=Name`. A page whose model has
/// none answers GET itself. A route convention (a registration whose rule has "razor-page-name" and
/// "razor-page-route" payloads, and "razor-page-area" for an area's page: AddPageRoute("/Shop/Index", "home")) gives
/// the page it names one more route, the template from the application's root, which replaces the page's own.
let razorPageEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let registrations = PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)

    let mapping =
        registrations |> List.filter (fun r -> hasRole "razor-pages" r.Rule)

    // Route conventions with constant arguments: the area, the page's name and the route.
    let conventions =
        registrations
        |> List.filter (fun r -> hasRole "razor-page-route" r.Rule && hasRole "razor-page-name" r.Rule)
        |> List.choose (fun r ->
            let evaluator =
                Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)

            let text (role: string) =
                r.Rule.Payload
                |> List.tryFind (fun p -> p.Role = role)
                |> Option.map (fun p ->
                    match evaluator.Operand(r.Offset, Values.operandIndex r.Declared false p.Argument) with
                    | Values.Text t -> Some t
                    | _ -> None)

            match text "razor-page-area", text "razor-page-name", text "razor-page-route" with
            | None, Some(Some page), Some(Some route) -> Some(None, page, route, r)
            | Some(Some area), Some(Some page), Some(Some route) -> Some(Some area, page, route, r)
            | _ -> None)

    let metadataRule =
        ctx.Matcher.Catalog.Rules
        |> List.tryFind (fun r -> r.Kind = RuleKind.Metadata && hasRole "razor-identifier" r)

    match mapping, metadataRule with
    | registration :: _, Some rule ->
        let keyOf (role: string) =
            rule.Payload |> List.tryFind (fun p -> p.Role = role) |> Option.bind (fun p -> p.Property)

        let metadata (t: TypeDef) (key: string option) =
            key
            |> Option.bind (fun k ->
                t.Attributes
                |> List.tryPick (fun a ->
                    match a.Arguments with
                    | [ AttributeValue.Text name; AttributeValue.Text value ] when a.Type.FullName = rule.Type && name = k -> Some value
                    | _ -> None))

        let registrationPoint = Instruction.pointAt registration.Caller.Body.Value.Value.Instructions registration.Index

        // The routes of a page file: `/Pages/Orders/Index.cshtml` is `/Orders/Index` and `/Orders`; and those the
        // conventions for its name (`/Orders/Index`, in its area) add, each with the convention that adds it.
        let routesOf (identifier: string) (template: string option) =
            let path = identifier.Replace('\\', '/')
            let withoutExtension = if path.EndsWith ".cshtml" then path.Substring(0, path.Length - 7) else path

            // The page's area, and its path under the area's (or the application's) Pages folder.
            let relative =
                let parts = withoutExtension.TrimStart('/').Split('/') |> Array.toList

                match parts with
                | "Pages" :: rest -> Some(None, rest)
                | "Areas" :: area :: "Pages" :: rest -> Some(Some area, rest)
                | _ -> None

            relative
            |> Option.map (fun (area, rest) ->
                let parts = Option.toList area @ rest

                let bases =
                    match List.rev parts with
                    | "Index" :: folder -> [ parts; List.rev folder ]
                    | _ -> [ parts ]

                let own =
                    bases
                    |> List.map (fun b ->
                        match template with
                        | Some t when t.StartsWith "/" || t.StartsWith "~/" -> PlatformRouting.normalizeRoute [ t.TrimStart('~') ]
                        | Some t when t <> "" -> PlatformRouting.normalizeRoute (b @ [ t ])
                        | _ -> PlatformRouting.normalizeRoute b)
                    |> List.distinct
                    |> List.map (fun route -> route, None)

                let name = "/" + String.concat "/" rest
                let same (a: string) (b: string) = System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

                let added =
                    conventions
                    |> List.filter (fun (a, page, _, _) ->
                        same page name
                        && match a, area with
                           | None, None -> true
                           | Some a, Some b -> same a b
                           | _ -> false)
                    |> List.map (fun (_, page, route, r) -> PlatformRouting.normalizeRoute [ route.TrimStart('~') ], Some(page, route, r))

                own @ added |> List.distinctBy fst)
            |> Option.defaultValue []

        let verbs = [ "Get"; "Post"; "Put"; "Delete"; "Patch"; "Head"; "Options" ]

        // A model's handler methods, with the HTTP method and the handler's name if it has one.
        let handlersOf (model: TypeRef) =
            (model :: set.BaseChain model)
            |> List.filter (fun t -> set.Origin t.Assembly <> Framework)
            |> List.choose set.TryType
            |> List.collect (fun (t, _) -> t.Methods |> List.filter (fun m -> m.IsPublic && not m.IsStatic && m.Ref.Name.StartsWith "On"))
            |> List.choose (fun m ->
                let name = m.Ref.Name.Substring 2
                let name = if name.EndsWith "Async" then name.Substring(0, name.Length - 5) else name

                verbs
                |> List.tryFind (fun v -> name.StartsWith v)
                |> Option.map (fun v ->
                    let handler = name.Substring v.Length
                    m, v.ToUpperInvariant(), (if handler = "" then None else Some handler)))
            |> List.distinctBy (fun (m, _, _) -> m.Ref.Name, m.Ref.ParameterTypes)

        ctx.Program.Users
        |> List.filter (fun (p, _) -> p.ProjectId = ctx.Project.ProjectId)
        |> List.collect (fun (_, a) -> a.Types)
        |> List.choose (fun t ->
            metadata t (keyOf "razor-identifier") |> Option.map (fun identifier -> t, identifier, metadata t (keyOf "razor-route-template")))
        |> List.collect (fun (page, identifier, template) ->
            let model =
                page.Methods
                |> List.tryFind (fun m -> m.Ref.Name = "get_Model")
                |> Option.bind (fun m -> set.TryType { Assembly = page.Ref.Assembly; FullName = m.Ref.ReturnType })
                |> Option.map (fun (d, _) -> d.Ref)

            let handlers =
                match model |> Option.map handlersOf with
                | Some(_ :: _ as hs) -> hs
                | _ ->
                    page.Methods
                    |> List.filter (fun m -> m.Ref.Name = "ExecuteAsync")
                    |> List.map (fun m -> m, "GET", None)

            routesOf identifier template
            |> List.collect (fun (route, convention) ->
                handlers
                |> List.choose (fun (m, httpMethod, named) ->
                    let handler = m.Ref
                    let point = firstPoint set handler
                    let location = registrationPoint |> Option.map Attribution.toLocation

                    let projectId =
                        match set.Origin handler.Type.Assembly with
                        | User pid -> pid
                        | _ -> ctx.Project.ProjectId

                    ctx.Program.Attributor.Attribute(projectId, handler, point)
                    |> Option.map (fun unit ->
                        let methodPredicate =
                            { Part = RequestPart.Method
                              Operator = PredicateOperator.Equals
                              Index = None
                              Value = httpMethod
                              Location = location }

                        let withTemplate =
                            match template with
                            | Some t -> " with the template \"" + t + "\""
                            | None -> ""

                        let forHandler =
                            match named with
                            | Some n -> " for the handler \"" + n + "\""
                            | None -> ""

                        let conventionStep =
                            convention
                            |> Option.map (fun (page, added, (r: PlatformRouting.Registration)) ->
                                step
                                    $"{r.Caller.Ref.Display} adds the route \"{added}\" to the page {page} with {r.Declared.Display} (rule {r.Rule.Id})"
                                    r.Caller.Ref
                                    (Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index))
                            |> Option.toList

                        let handlerPredicate =
                            named
                            |> Option.map (fun n ->
                                { Part = RequestPart.Query
                                  Operator = PredicateOperator.Equals
                                  Index = None
                                  Value = $"handler={n}"
                                  Location = location })
                            |> Option.toList

                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = handler.Display
                          Location = point |> Option.map Attribution.toLocation
                          Category = EffectCategory.HttpIn
                          RegisteredThrough = None
                          Predicates = methodPredicate :: PlatformRouting.templatePredicates route location @ handlerPredicate
                          HttpMethod = Some httpMethod
                          Route = Some route
                          Completeness = Completeness.Complete
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{registration.Caller.Ref.Display} maps Razor Pages with {registration.Declared.Display} (rule {registration.Rule.Id})" registration.Caller.Ref registrationPoint
                                  step $"{page.Ref.FullName} is the page {identifier}{withTemplate} (rule {rule.Id})" handler None
                                  yield! conventionStep
                                  step $"{handler.Display} handles its {httpMethod} requests{forHandler}" handler point ] } }))))
    | _ -> []

// ---- Razor components (Blazor) ----

/// Routed Razor components, mapped by a registration whose rule has a "components" payload: MapRazorComponents<T>()
/// maps the components in the assembly of its root component (the type argument at the payload's index), and
/// MapBlazorHub() those of the application. A component is routed by the attributes whose metadata rule has a
/// "component-route" payload (`@page "/orders/{Id:int}"` compiles to [Route("/orders/{Id:int}")]), one entry point per
/// template, at GET. Its handler is the first lifecycle method the component defines (OnInitializedAsync,
/// OnInitialized, OnParametersSetAsync, OnParametersSet), or else its BuildRenderTree.
let componentEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let lifecycle = [ "OnInitializedAsync"; "OnInitialized"; "OnParametersSetAsync"; "OnParametersSet"; "BuildRenderTree" ]

    let routeRules =
        ctx.Matcher.Catalog.Rules
        |> List.filter (fun r -> r.Kind = RuleKind.Metadata && hasRole "component-route" r)

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.filter (fun r -> hasRole "components" r.Rule)
    |> List.tryHead
    |> Option.map (fun r ->
        let spec = r.Rule.Payload |> List.find (fun p -> p.Role = "components")
        let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index
        let location = registrationPoint |> Option.map Attribution.toLocation

        let assembly =
            match List.tryItem spec.Argument r.Declared.TypeArguments with
            | Some root -> root.Assembly
            | None -> System.IO.Path.GetFileNameWithoutExtension ctx.Project.AssemblyPath

        match set.TryLoad assembly with
        | None -> []
        | Some a ->
            a.Types
            |> List.collect (fun t ->
                t.Attributes
                |> List.choose (fun at ->
                    routeRules
                    |> List.tryPick (fun rule -> if at.Type.FullName = rule.Type then textArgument rule "component-route" at else None))
                |> List.distinct
                |> List.map (fun template -> t, template))
            |> List.choose (fun (component, template) ->
                let handler =
                    lifecycle
                    |> List.tryPick (fun name -> component.Methods |> List.tryFind (fun m -> m.Ref.Name = name && not m.IsStatic))
                    |> Option.map (fun m -> m.Ref)

                handler
                |> Option.bind (fun handler ->
                    let point = firstPoint set handler

                    let projectId =
                        match set.Origin handler.Type.Assembly with
                        | User pid -> pid
                        | _ -> ctx.Project.ProjectId

                    ctx.Program.Attributor.Attribute(projectId, handler, point)
                    |> Option.map (fun unit ->
                        let route = PlatformRouting.normalizeRoute [ template ]

                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = handler.Display
                          Location = point |> Option.map Attribution.toLocation
                          Category = EffectCategory.HttpIn
                          RegisteredThrough = None
                          Predicates =
                            { Part = RequestPart.Method
                              Operator = PredicateOperator.Equals
                              Index = None
                              Value = "GET"
                              Location = location }
                            :: PlatformRouting.templatePredicates route location
                          HttpMethod = Some "GET"
                          Route = Some route
                          Completeness = Completeness.Complete
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{r.Caller.Ref.Display} maps the routed components of {assembly} with {r.Declared.Display} (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                                  step $"{component.Ref.FullName} is routed at \"{template}\"; {handler.Display} runs when it is rendered" handler point ] } }))))
    |> Option.defaultValue []

/// The path of the circuit's hub, over which an interactive component's events arrive: Blazor's default.
let private circuitPath = "/_blazor"

/// Interactive Razor components' events: with interactive server rendering on (a registration whose rule has a
/// "component-events" payload: AddInteractiveServerRenderMode(), MapBlazorHub()), the browser sends a component's
/// events over the circuit's hub, and each runs the handler the component bound it to. The generated
/// BuildRenderTree binds an event by a call whose metadata rule has "component-event-name" and
/// "component-event-handler" payloads (RenderTreeBuilder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this,
/// Increment))): an "on..." name, and a value made from a delegate to the handler (a method, a lambda, the setter
/// lambda @bind makes). One entry point per event and handler, at the hub's path: the handler is chosen by the
/// circuit's message, not the request, so the route is partial. Every component of the mapped assembly counts,
/// whether or not it is rendered interactively.
let componentEventEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set
    let registrations = PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)

    let eventRules =
        ctx.Matcher.Catalog.Rules
        |> List.filter (fun r -> r.Kind = RuleKind.Metadata && hasRole "component-event-name" r && hasRole "component-event-handler" r)

    // The handler a value's delegate runs: in the value itself, or in the arguments of the calls that make it
    // (EventCallback.Factory.Create(this, handler), CreateBinder(this, setter, value)).
    let rec handlersIn (flow: Flow) (depth: int) (values: Values) : MethodRef list =
        match delegateTargets flow values with
        | [] when depth < 3 ->
            values
            |> Set.toList
            |> List.collect (function
                | Pushed o ->
                    match flow.InstructionAt o with
                    | Some { Op = (Call _ | CallVirt _ | NewObj _) } -> flow.OperandsAt o |> List.collect (handlersIn flow (depth + 1))
                    | _ -> []
                | _ -> [])
            |> List.distinctBy (fun m -> m.Key)
        | found -> found

    let components =
        registrations
        |> List.tryFind (fun r -> hasRole "components" r.Rule)

    let events =
        registrations
        |> List.tryFind (fun r -> hasRole "component-events" r.Rule)

    match components, events with
    | Some c, Some ev ->
        let spec = c.Rule.Payload |> List.find (fun p -> p.Role = "components")

        let assembly =
            match List.tryItem spec.Argument c.Declared.TypeArguments with
            | Some root -> root.Assembly
            | None -> System.IO.Path.GetFileNameWithoutExtension ctx.Project.AssemblyPath

        let registrationPoint = Instruction.pointAt ev.Caller.Body.Value.Value.Instructions ev.Index
        let location = registrationPoint |> Option.map Attribution.toLocation
        let route = PlatformRouting.normalizeRoute [ circuitPath ]

        match set.TryLoad assembly with
        | None -> []
        | Some a ->
            [ for t in a.Types do
                  for render in t.Methods |> List.filter (fun m -> m.Ref.Name = "BuildRenderTree" && not m.IsStatic) do
                      match render.Body.Value, DataFlow.ofMethod render with
                      | Some body, Some flow ->
                          let evaluator =
                              Values.Evaluator(flow, ctx.Program.Recognizers, Values.constantReturn set, (fun _ _ -> Values.Origin.unknown), ctx.Program.SettingsFor ctx.Project)

                          for index in 0 .. body.Instructions.Length - 1 do
                              let ins = body.Instructions[index]

                              match ins.Op with
                              | Call d
                              | CallVirt d ->
                                  for rule in eventRules |> List.filter (fun r -> ctx.Matcher.Match d |> List.exists (fun m -> m.Id = r.Id)) do
                                      let nameSpec = rule.Payload |> List.find (fun p -> p.Role = "component-event-name")
                                      let handlerSpec = rule.Payload |> List.find (fun p -> p.Role = "component-event-handler")

                                      match evaluator.Operand(ins.Offset, Values.operandIndex d false nameSpec.Argument) with
                                      | Values.Text name when name.StartsWith "on" ->
                                          let handlers =
                                              List.tryItem (Values.operandIndex d false handlerSpec.Argument) (flow.OperandsAt ins.Offset)
                                              |> Option.map (handlersIn flow 0)
                                              |> Option.defaultValue []

                                          let bound = Instruction.pointAt body.Instructions index

                                          for handler in handlers do
                                              let point = firstPoint set handler

                                              let projectId =
                                                  match set.Origin handler.Type.Assembly with
                                                  | User pid -> pid
                                                  | _ -> ctx.Project.ProjectId

                                              match ctx.Program.Attributor.Attribute(projectId, handler, point) with
                                              | Some unit ->
                                                  yield
                                                      { Id = ""
                                                        ApplicationId = ctx.Project.ProjectId
                                                        HandlerUnit = unit.Id
                                                        HandlerMember = handler.Display
                                                        Location = point |> Option.map Attribution.toLocation
                                                        Category = EffectCategory.HttpIn
                                                        RegisteredThrough = None
                                                        Predicates = PlatformRouting.templatePredicates route location
                                                        HttpMethod = None
                                                        Route = Some route
                                                        Completeness = Completeness.Partial
                                                        Forms = [ RouteForm.PlatformRouting ]
                                                        Evidence =
                                                          { Steps =
                                                              [ step
                                                                    $"{ev.Caller.Ref.Display} lets components handle the browser's events over the circuit at {circuitPath} with {ev.Declared.Display} (rule {ev.Rule.Id})"
                                                                    ev.Caller.Ref
                                                                    registrationPoint
                                                                step
                                                                    $"{t.Ref.FullName} binds its \"{name}\" event to {handler.Display} with {d.Display} (rule {rule.Id})"
                                                                    render.Ref
                                                                    bound
                                                                step $"{handler.Display} runs when the browser sends the event" handler point ] } }
                                              | None -> ()
                                      | _ -> ()
                              | _ -> ()
                      | _ -> () ]
            |> List.distinctBy (fun e -> e.HandlerMember, e.Evidence.Steps[1].Description)
    | _ -> []

// ---- Handlers found by scanning ----

/// Handlers a library finds by scanning the application's assembly, with no registration of their own: a
/// registration whose rule has a "scanned-handlers" payload (NServiceBus's Endpoint.Start) makes every type of the
/// application that implements a member of the callback rule for the type the payload's property names a handler
/// (IHandleMessages<T>.Handle). Each is an entry point at the message type its first parameter is.
let scannedEntries (ctx: AppContext) : EntryPoint list =
    let set = ctx.Program.Set

    PlatformRouting.registrations ctx.Matcher (List.ofSeq ctx.Methods.Values)
    |> List.filter (fun r -> hasRole "scanned-handlers" r.Rule)
    |> List.tryHead
    |> Option.map (fun r ->
        let category = defaultArg r.Rule.Category EffectCategory.Network
        let registrationPoint = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index
        let scanned = (r.Rule.Payload |> List.find (fun p -> p.Role = "scanned-handlers")).Property

        ctx.Program.Users
        |> List.filter (fun (p, _) -> p.ProjectId = ctx.Project.ProjectId)
        |> List.collect (fun (_, a) -> a.Types)
        |> List.filter (fun t -> not t.IsAbstract && not t.IsInterface && not t.IsCompilerGenerated)
        |> List.collect (fun t ->
            ctx.Matcher.Callbacks t.Ref
            |> List.filter (fun rule -> rule.Category = Some category && Some rule.Type = scanned)
            |> List.collect (fun rule ->
                match set.TryType { Assembly = rule.Assembly; FullName = rule.Type } with
                | None -> []
                | Some(declaring, _) ->
                    // A generic interface implemented for several message types gives one method per type.
                    t.Methods
                    |> List.filter (fun m ->
                        m.IsPublic
                        && not m.IsStatic
                        && rule.Members |> List.exists (fun p -> Matcher.MemberMatches p m.Ref.Name)
                        && declaring.Methods |> List.exists (fun d -> d.Ref.Name = m.Ref.Name && d.Ref.ParameterCount = m.Ref.ParameterCount))
                    |> List.map (fun m -> t, rule, m)))
        |> List.distinctBy (fun (_, _, m) -> m.Ref.Key)
        |> List.choose (fun (t, rule, m) ->
            let handler = m.Ref
            let point = firstPoint set handler

            let projectId =
                match set.Origin handler.Type.Assembly with
                | User pid -> pid
                | _ -> ctx.Project.ProjectId

            ctx.Program.Attributor.Attribute(projectId, handler, point)
            |> Option.map (fun unit ->
                let message = handler.ParameterTypes |> List.tryHead
                let messageName = defaultArg message "message"

                { Id = ""
                  ApplicationId = ctx.Project.ProjectId
                  HandlerUnit = unit.Id
                  HandlerMember = handler.Display
                  Location = point |> Option.map Attribution.toLocation
                  Category = category
                  RegisteredThrough = Some rule.Type
                  Predicates = []
                  HttpMethod = None
                  Route = message
                  Completeness = if message.IsSome then Completeness.Complete else Completeness.Unresolved
                  Forms = [ RouteForm.PlatformRouting ]
                  Evidence =
                    { Steps =
                        [ step $"{r.Caller.Ref.Display} starts {r.Declared.Display}, which finds the handlers of the application by scanning it (rule {r.Rule.Id})" r.Caller.Ref registrationPoint
                          step $"{t.Ref.FullName} implements {rule.Type}: {handler.Display} handles each {messageName} (rule {rule.Id})" handler point ] } })))
    |> Option.defaultValue []
