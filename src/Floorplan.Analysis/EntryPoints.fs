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

                for e in program.Graph.Callees m do
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
        ctx.Program.Graph.Callees m.Ref
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

    let guarded =
        ctx.Methods.Values
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

    let receivers =
        ctx.Taint.Sources
        |> List.filter (fun s -> ctx.Methods.ContainsKey s.Method.Key)
        |> List.distinctBy (fun s -> s.Method.Key)
        |> List.collect (fun s ->
            // Everything downstream of the source, through calls that pass request data along.
            let downstream = HashSet<string>([ s.Method.Key ])
            let stack = Stack<string>([ s.Method.Key ])

            while stack.Count > 0 do
                let k = stack.Pop()

                for e in ctx.Program.Graph.Callees ctx.Methods[k].Ref do
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
            let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set)
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
/// `/Home` and `/`). None when the pattern does not reach the action: no controller or action parameter, or an area
/// that does not match the controller's.
let conventionalRoutes (pattern: string) (requiredArea: string option) (controller: string) (action: string) (area: string option) =
    let segments = parsePattern pattern
    let has name = segments |> List.exists (function Parameter(n, _, _, _) -> n = name | _ -> false)

    let areaMatches =
        if has "area" then area.IsSome
        else
            match requiredArea with
            | Some a -> area |> Option.exists (fun x -> System.String.Equals(x, a, System.StringComparison.OrdinalIgnoreCase))
            | None -> area.IsNone

    if not (has "controller" && has "action") || not areaMatches then
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
            | Parameter(n, Some d, _, _) -> values.TryFind n |> Option.exists (fun v -> System.String.Equals(v, d, System.StringComparison.OrdinalIgnoreCase))
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
        let attributeMapping = mapping |> List.tryFind (fun r -> hasRole "controllers" r.Rule)
        let attributeRouting = attributeMapping.IsSome
        let pointOf (r: PlatformRouting.Registration) = Instruction.pointAt r.Caller.Body.Value.Value.Instructions r.Index

        let operandText (r: PlatformRouting.Registration) (role: string) =
            r.Rule.Payload
            |> List.tryFind (fun p -> p.Role = role)
            |> Option.bind (fun spec ->
                let evaluator = Values.Evaluator(r.Flow, ctx.Program.Recognizers, Values.constantReturn set)

                match evaluator.Operand(r.Offset, Values.operandIndex r.Declared false spec.Argument) with
                | Values.Text t -> Some t
                | _ -> None)

        // Each conventional route: its pattern (None when not a constant), the area it requires, and where it is mapped.
        let conventional =
            mapping
            |> List.filter (fun r -> hasRole "conventional-controllers" r.Rule)
            |> List.map (fun r ->
                let pattern =
                    match operandText r "route-pattern" with
                    | Some p -> Some p
                    | None -> if hasRole "route-pattern" r.Rule then None else r.Rule.ComparesTo

                pattern, operandText r "area-name", r)

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

            controller.Methods
            |> List.filter (fun m ->
                m.IsPublic
                && not m.IsStatic
                && not m.IsConstructor
                && not m.IsAbstract
                && not m.IsSpecialName
                && not m.IsCompilerGenerated
                && (attributesWith "not-an-action" m.Attributes).IsEmpty)
            |> List.collect (fun action ->
                let actionName = action.Ref.Name
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
                    if routed && attributeRouting then
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
                    elif routed then
                        // Attribute routes without MapControllers: MVC does not map them.
                        []
                    else
                        let methods = actionVerbs |> List.collect fst |> List.distinct

                        let byConvention =
                            conventional
                            |> List.collect (fun (pattern, requiredArea, r) ->
                                match pattern with
                                | Some p ->
                                    conventionalRoutes p requiredArea controllerName actionName area
                                    |> List.map (fun route -> Some route, $"the conventional route \"{p}\"", r)
                                | None -> [ None, "a conventional route whose pattern is not a constant", r ])
                            |> List.distinctBy (fun (route, _, _) -> route)

                        match byConvention, attributeMapping with
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

                        let completeness =
                            match httpMethod, route with
                            | Some _, Some _ -> Completeness.Complete
                            | None, None -> Completeness.Unresolved
                            | _ -> Completeness.Partial

                        { Id = ""
                          ApplicationId = ctx.Project.ProjectId
                          HandlerUnit = unit.Id
                          HandlerMember = action.Ref.Display
                          Location = location
                          Category = EffectCategory.HttpIn
                          Predicates = methodPredicate @ routePredicates
                          HttpMethod = httpMethod
                          Route = route
                          Completeness = completeness
                          Forms = [ RouteForm.PlatformRouting ]
                          Evidence =
                            { Steps =
                                [ step $"{registration.Caller.Ref.Display} maps controllers with {registration.Declared.Display} (rule {registration.Rule.Id})" registration.Caller.Ref (pointOf registration)
                                  step $"{action.Ref.Display} is an action of controller {controller.Ref.FullName}, routed by {how}" action.Ref point ] } })))
