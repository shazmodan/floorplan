/// Exit points (plan section 5): call sites in user code whose target is a catalog sink, or a dependency method
/// whose summary reaches one. Each is attributed to the unit containing the call site and keeps the shortest
/// call path to the primitive as evidence.
module Floorplan.Analysis.ExitPoints

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog
open Floorplan.Analysis.Summaries

type ProjectContext =
    { Project: AnalyzedProject
      Set: AssemblySet
      Graph: CallGraph
      Matcher: Matcher
      /// Rules a dependency method reaches, from the call graph or the summary cache.
      Effects: MethodRef -> DependencyEffect list
      /// What a callback member reaches through its constructed implementations, with each one's assembly.
      Dispatch: string -> (string * DependencyEffect) list
      /// What a call to a member that loads a configuration file makes: for each object the file names, what
      /// names it and the method that makes it.
      Loaded: MethodRef -> (string * MethodRef) list
      Attributor: Attribution.Attributor
      Recognizers: ICodegenRecognizer list
      /// What constructors and other methods set on the objects they produce, read from their bodies.
      Producers: Values.Producers
      /// The application's configuration, for values read from it.
      Settings: Values.Settings }

/// An exit point before ids are assigned.
type Found =
    { ExitPoint: ExitPoint
      SortKey: string * string * int * int * string }

/// A log call: a call site whose declared target matches a catalog logger rule.
type private LogSite =
    { Caller: MethodRef
      Offset: int
      Point: SourcePoint option
      Logger: MethodRef }

/// The log calls of one unit that reach the same effect, and the shortest route there.
type private LogGroup =
    { Unit: string
      Category: EffectCategory
      Payload: Payload option
      ViaAssembly: string
      Sites: ResizeArray<LogSite>
      mutable Effect: DependencyEffect
      mutable EffectSite: LogSite }

let private step (description: string) (m: MethodRef) (point: SourcePoint option) =
    { Description = description
      Member = m.Display
      Assembly = m.Type.Assembly
      Location = point |> Option.map Attribution.toLocation }

let private categoryName (c: EffectCategory) = EffectCategory.name c

/// A payload as fixed in a dependency's code, whatever the call's arguments: only its literal parts are known.
let private fixedPayload (d: DependencyEffect) =
    d.Payload
    |> Option.map (fun p ->
        let parts =
            p.Origin
            |> List.map (function
                | Values.Literal t -> Values.Text t
                | _ -> Values.Hole)

        Values.Evaluator.ToPayload(Values.Parts parts, p.Role))

/// Confidence from the evidence path through a dependency: each call is one step, each virtual call one more,
/// because virtual dispatch is where a static call graph over-approximates.
/// Of the effects a dependency reaches in one category, with their payloads, those whose payload is known; one
/// that says what the effect does (its SQL) before one that says only where it goes (a connection string).
let private preferred (resolved: ('d * Payload option) list) =
    let known = resolved |> List.filter (fun (_, p) -> p |> Option.exists (fun p -> p.Kind <> PayloadKind.Dynamic))

    match known |> List.filter (fun (_, p) -> p |> Option.exists (fun p -> p.Role <> "connection")) with
    | [] -> known
    | what -> what

/// Of the database effects a dependency reaches, the connection it opens (Dapper's Open of the connection it is
/// given), when the effect shown says something else (its SQL): the connection string's literal, secrets replaced.
let private opened (resolved: ('d * Payload option) list) (shown: Payload option) =
    match shown with
    | Some { Role = "connection" } -> None
    | _ ->
        resolved
        |> List.tryPick (fun (_, p) ->
            match p with
            | Some { Role = "connection"; Kind = PayloadKind.Literal; Value = value } -> value
            | _ -> None)

let private confidenceOfScore (score: int) =
    if score <= 4 then Confidence.High
    elif score <= 12 then Confidence.Medium
    else Confidence.Low

let confidenceOf (path: PathStep list) = confidenceOfScore (scoreOf path)

/// The call sites of a method body, with the edges the call graph resolved for each.
let private callSites (ctx: ProjectContext) (m: MethodDef) (body: MethodBody) =
    let edges = ctx.Graph.Callees m.Ref |> List.groupBy (fun e -> e.Offset) |> dict

    body.Instructions
    |> Array.mapi (fun i ins -> i, ins)
    |> Array.choose (fun (i, ins) ->
        match ins.Op with
        | Call declared
        | CallVirt declared
        | NewObj declared ->
            let resolved =
                match edges.TryGetValue ins.Offset with
                | true, es -> es |> List.filter (fun e -> e.Kind = Direct || e.Kind = Virtual)
                | _ -> []

            let resolved =
                if resolved.IsEmpty then
                    [ { Caller = m.Ref
                        Offset = ins.Offset
                        Declared = declared
                        Target = declared
                        Kind = Direct } ]
                else
                    resolved

            Some(i, ins, declared, resolved)
        | _ -> None)

/// The logger rule for an attribute among these, if one marks log methods ([LoggerMessage]).
let private loggerAttribute (ctx: ProjectContext) (attributes: AttributeDef list) =
    attributes |> List.tryPick (fun a -> ctx.Matcher.LoggerAttribute a.Type.FullName)

/// Log methods whose bodies a source generator writes: a call to one is the log call (logger.microsoft-generated).
let private generatedLogger (ctx: ProjectContext) (declared: MethodRef) =
    match ctx.Set.Origin declared.Type.Assembly with
    | Framework -> None
    | _ ->
        match ctx.Set.TryMethod declared with
        | Some(def, _) -> loggerAttribute ctx def.Attributes
        | None -> None

let analyze (ctx: ProjectContext) (assembly: AssemblyDef) : Found list * Diagnostic list =
    let found = ResizeArray<Found>()

    // The application assemblies that share this one's memory cache: it and the ones of the solution it references.
    let applications =
        let rec reach (seen: Set<string>) (name: string) =
            if seen.Contains name then
                seen
            else
                match ctx.Set.TryLoad name with
                | Some a when ctx.Set.IsUser a.Name -> a.ReferencedAssemblies |> List.fold reach (seen.Add a.Name)
                | _ -> seen

        reach Set.empty assembly.Name |> Set.toList

    let keeping = ctx.Producers.Keeping applications

    let evaluatorOf (flow: DataFlow.Flow) =
        Values.Evaluator(flow, ctx.Recognizers, Values.constantReturn ctx.Set, (fun m n -> ctx.Producers.Member(m, n)), ctx.Settings, keeping)
    let diagnostics = ResizeArray<Diagnostic>()
    let projectId = ctx.Project.ProjectId

    // The call to a logger rule's member that made a delegate (LoggerMessage.Define), among these values.
    let madeBy (flow: DataFlow.Flow) (values: DataFlow.Values) =
        values
        |> Set.toList
        |> List.tryPick (function
            | DataFlow.Pushed o ->
                match flow.InstructionAt o with
                | Some { Op = Call c } -> ctx.Matcher.LoggerDelegate c |> Option.map (fun r -> r, c)
                | _ -> None
            | _ -> None)

    // The delegate a field holds, when its declaring type stores one made by a logger rule's member there (a
    // static field set in the static constructor, as the [LoggerMessage] generator and hand-written code do).
    let storedDelegates = Dictionary<string, (Rule * MethodRef) option>()

    let storedIn (f: FieldRef) =
        match storedDelegates.TryGetValue f.Key with
        | true, r -> r
        | _ ->
            let stores (ins: Instruction) =
                match ins.Op with
                | StSFld g
                | StFld g -> g.Key = f.Key
                | _ -> false

            let found =
                match ctx.Set.TryType f.Type with
                | Some(t, _) ->
                    t.Methods
                    |> List.tryPick (fun md ->
                        match md.Body.Value with
                        | Some b when b.Instructions |> Array.exists stores ->
                            DataFlow.ofMethod md
                            |> Option.bind (fun fl ->
                                b.Instructions
                                |> Array.filter stores
                                |> Array.tryPick (fun i -> fl.OperandsAt i.Offset |> List.tryLast |> Option.bind (madeBy fl)))
                        | _ -> None)
                | None -> None

            storedDelegates[f.Key] <- found
            found

    // A call that invokes a delegate made by a logger rule's member: the rule and the member.
    let invokedLogger (flow: DataFlow.Flow) (offset: int) (declared: MethodRef) =
        if declared.Name <> "Invoke" || not declared.HasThis then
            None
        else
            match flow.OperandsAt offset with
            | receiver :: _ ->
                match madeBy flow receiver with
                | Some r -> Some r
                | None ->
                    receiver
                    |> Set.toList
                    |> List.tryPick (function
                        | DataFlow.Pushed o ->
                            match flow.InstructionAt o with
                            | Some { Op = (LdSFld f | LdFld f) } -> storedIn f
                            | _ -> None
                        | _ -> None)
            | [] -> None

    // The SQL a query runs, when the call that made it has it (FromSqlRaw's argument): in the method that made it,
    // then at each call that returned the query, for what was a parameter there.
    let queryPayload (q: Queries.QueryOrigin) =
        match q.Maker, q.Rule.Payload with
        | Some(flow, offset, maker), spec :: _ ->
            let made = (evaluatorOf flow).Operand(offset, Values.operandIndex maker false spec.Argument)

            let value =
                q.Calls
                |> List.fold (fun v (caller, call) -> (evaluatorOf caller).Resolve(Values.Origin.ofSymbolic v, call, false)) made

            Some(Values.Evaluator.ToPayload(value, spec.Role))
        | _ -> None

    // Following a query into the methods that return it: bodies outside the shared framework, read once.
    let bodies = Dictionary<string, (MethodDef * DataFlow.Flow) option>()
    let efContexts = EfContexts.Contexts(ctx.Set, assembly)

    let queries: Queries.Context =
        { Matcher = ctx.Matcher
          Body =
            fun m ->
                match bodies.TryGetValue m.Key with
                | true, b -> b
                | _ ->
                    let b =
                        match ctx.Set.Origin m.Type.Assembly with
                        | Framework -> None
                        | _ ->
                            ctx.Set.TryMethod m
                            |> Option.bind (fun (def, _) -> DataFlow.ofMethod def |> Option.map (fun f -> def, f))

                    bodies[m.Key] <- b
                    b
          Targets =
            fun caller offset ->
                ctx.Graph.Callees caller
                |> List.filter (fun e -> e.Offset = offset && (e.Kind = Direct || e.Kind = Virtual))
                |> List.map (fun e -> e.Target)
          Generated =
            fun t ->
                match ctx.Set.Origin t.Assembly with
                | Framework -> None
                | _ ->
                    match ctx.Set.TryType t with
                    | Some(def, _) when def.IsCompilerGenerated -> Some(def.Methods |> List.map (fun m -> m.Ref))
                    | _ -> None }

    let enumeratedParameters = Queries.enumeratedParameters queries

    // What a logger's sinks do is the same at every log call: one exit point per unit and effect, listing the calls.
    let logGroups = Dictionary<string * EffectCategory * string * string * Payload option * string, LogGroup>()

    let sortKey (unitId: string) (location: Location option) (offset: int) (category: EffectCategory) (ruleId: string) =
        (unitId,
         (match location with Some l -> l.File | None -> ""),
         (match location with Some l -> l.StartLine | None -> 0),
         offset,
         categoryName category + ruleId)

    let unattributed (category: EffectCategory) (m: MethodDef) (point: SourcePoint option) =
        diagnostics.Add
            { Severity = DiagnosticSeverity.Warning
              Code = "exit-point.unattributed"
              Message = $"A {categoryName category} call in {m.Ref.Display} could not be attributed to a unit."
              ProjectId = Some projectId
              Location = point |> Option.map Attribution.toLocation }

    let dependencySteps (d: DependencyEffect) (category: EffectCategory) =
        [ yield!
              d.Steps
              |> List.map (fun s ->
                  { Description = $"{s.Caller} calls {s.Declared}"
                    Member = s.Caller
                    Assembly = s.CallerAssembly
                    Location = None })
          { Description = $"{d.Primitive} is a {categoryName category} primitive (rule {d.RuleId})"
            Member = d.Primitive
            Assembly = d.PrimitiveAssembly
            Location = None } ]

    let marked (d: MethodDef) =
        d.Attributes |> List.exists (fun a -> (ctx.Matcher.MarkingSink a.Type).IsSome)

    // Methods of a proxy that implement an interface method an attribute marks as the effect, each with that method:
    // the implementation Refit's source generator writes (code not the user's), or one that forwards the call to
    // the marked method of another object (a WCF ClientBase<T> proxy calls its Channel's). The effect is reported
    // at the calls to either, not again inside. Another implementation (a WCF service's) is code of its own.
    let proxyMethodsOf (t: TypeDef) : Map<string, MethodDef> =
        let notUsers =
            t.IsCompilerGenerated
            || t.Attributes
               |> List.exists (fun a ->
                   a.Type.FullName = "System.Diagnostics.DebuggerNonUserCodeAttribute"
                   || a.Type.FullName = "System.CodeDom.Compiler.GeneratedCodeAttribute")

        let forwards (m: MethodDef) =
            match m.Body.Value with
            | Some body ->
                body.Instructions
                |> Array.exists (fun ins ->
                    match ins.Op with
                    | Call c
                    | CallVirt c ->
                        match ctx.Set.TryMethod c with
                        | Some(d, _) -> marked d
                        | None -> false
                    | _ -> false)
            | None -> false

        t.Interfaces
        |> List.choose ctx.Set.TryType
        |> List.collect (fun (i, _) ->
            i.Methods
            |> List.filter marked
            |> List.collect (fun im ->
                t.Methods
                |> List.filter (fun m ->
                    m.Overrides |> List.exists (fun o -> o.Key = im.Ref.Key)
                    || (m.Ref.Name = im.Ref.Name && m.Ref.ParameterTypes = im.Ref.ParameterTypes))
                |> List.filter (fun m -> notUsers || forwards m)
                |> List.map (fun m -> m.Ref.Key, im)))
        |> Map.ofList

    let proxyMethods = Dictionary<string, Map<string, MethodDef>>()

    // The marked interface method a call of `declared` is: the method itself, or the one a proxy's method implements.
    let markedTarget (declared: MethodRef) : MethodDef option =
        match ctx.Set.TryMethod declared with
        | Some(def, _) when marked def -> Some def
        | Some _ ->
            match ctx.Set.TryType declared.Type with
            | Some(t, _) when not t.IsInterface ->
                let methods =
                    match proxyMethods.TryGetValue t.Ref.FullName with
                    | true, ms -> ms
                    | _ ->
                        let ms = proxyMethodsOf t
                        proxyMethods[t.Ref.FullName] <- ms
                        ms

                methods.TryFind declared.Key
            | _ -> None
        | None -> None

    // The values, with a field of `this` the method itself stores read as the value stored: an async method keeps its
    // locals in fields of its state machine.
    let stored (f: DataFlow.Flow) (values: DataFlow.Values) =
        values
        |> Set.toList
        |> List.collect (fun v ->
            match v with
            | DataFlow.Pushed o ->
                match f.InstructionAt o, f.OperandsAt o with
                | Some { Op = LdFld fld }, [ owner ] when owner.Contains(DataFlow.Argument 0) ->
                    let stores =
                        f.Body.Instructions
                        |> Array.toList
                        |> List.choose (fun ins ->
                            match ins.Op, f.OperandsAt ins.Offset with
                            | StFld g, [ target; value ] when g.Key = fld.Key && target.Contains(DataFlow.Argument 0) -> Some value
                            | _ -> None)

                    // The state machine clears a field it is done with: a null stored is no value of it.
                    let isNull (s: DataFlow.Value) =
                        match s with
                        | DataFlow.Pushed so -> (match f.InstructionAt so with Some { Op = LdNull } -> true | _ -> false)
                        | _ -> false

                    match stores |> List.collect Set.toList |> List.filter (isNull >> not) with
                    | [] -> [ v ]
                    | values -> values
                | _ -> [ v ]
            | _ -> [ v ])

    // A call another application answers through a sidecar (Dapr's service invocation): the application's id as the
    // host, then the method, the argument after it.
    let appMethod (f: DataFlow.Flow) (offset: int) (index: int) =
        let evaluator = evaluatorOf f
        Values.Parts [ Values.Text "http://"; evaluator.Operand(offset, index); Values.Text "/"; evaluator.Operand(offset, index + 1) ]

    // The value a configuration delegate among a call's arguments sets a property to (AddOtlpExporter(o =>
    // o.Endpoint = new Uri("http://collector:4317"))): the delegate's method, from the ldftn it is made with, and in
    // it a call of the property's setter.
    let configuredValue (f: DataFlow.Flow) (offset: int) (property: string) =
        let target (values: DataFlow.Values) =
            values
            |> Set.toList
            |> List.tryPick (function
                | DataFlow.Pushed o ->
                    match f.InstructionAt o with
                    | Some { Op = NewObj _ } ->
                        f.OperandsAt o
                        |> List.tryPick (fun vs ->
                            vs
                            |> Set.toList
                            |> List.tryPick (function
                                | DataFlow.Pushed fo ->
                                    match f.InstructionAt fo with
                                    | Some { Op = (LdFtn m | LdVirtFtn m) } -> Some m
                                    | _ -> None
                                | _ -> None))
                    | _ -> None
                | _ -> None)

        let setIn (configure: MethodRef) =
            match ctx.Set.TryMethod configure with
            | Some(def, _) ->
                match def.Body.Value, DataFlow.ofMethod def with
                | Some body, Some cf ->
                    body.Instructions
                    |> Array.tryPick (fun ins ->
                        match ins.Op with
                        | CallVirt setter
                        | Call setter when setter.Name = "set_" + property && setter.ParameterCount = 1 ->
                            match (evaluatorOf cf).Operand(ins.Offset, 1) with
                            | Values.Hole -> None
                            | v -> Some v
                        | _ -> None)
                | _ -> None
            | None -> None

        f.OperandsAt offset |> List.tryPick (fun v -> target v |> Option.bind setIn) |> Option.defaultValue Values.Hole

    // The payload of a direct call to a primitive, worked out in the calling method's data flow.
    let sinkPayload (f: DataFlow.Flow) (offset: int) (declared: MethodRef) (isNewObj: bool) (hit: SinkHit) : Payload option =
        hit.Rule.Payload
        |> List.tryHead
        |> Option.map (fun spec ->
            let evaluator = evaluatorOf f

            match spec.Property with
            | _ when spec.Role = "grain-method" ->
                // A call to a grain (Orleans): addressed by its interface and method, as the grain's entry point is.
                Values.Evaluator.ToPayload(Values.Text $"{declared.Type.FullName}.{declared.Name}", spec.Role)
            | _ when spec.Role = "grpc-method" ->
                // A gRPC call's method descriptor: /{service}/{method}, from the constructor arguments the catalog's
                // descriptor rule names.
                let descriptor =
                    ctx.Matcher.Catalog.Rules
                    |> List.tryFind (fun r -> r.Payload |> List.exists (fun p -> p.Role = "grpc-service-name"))

                let path =
                    match descriptor, List.tryItem (Values.operandIndex declared isNewObj spec.Argument) (f.OperandsAt offset) with
                    | Some rule, Some operand ->
                        let index (role: string) = (rule.Payload |> List.find (fun p -> p.Role = role)).Argument

                        match Values.staticObjectTexts ctx.Set f operand rule.Type [ index "grpc-service-name"; index "grpc-method-name" ] with
                        | Some [ service; name ] -> Values.Text $"/{service}/{name}"
                        | _ -> Values.Hole
                    | _ -> Values.Hole

                Values.Evaluator.ToPayload(path, "url")
            | Some property when spec.Role = "configured-url" ->
                Values.Evaluator.ToPayload(configuredValue f offset property, "url")
            | _ when spec.Role = "app-method" ->
                Values.Evaluator.ToPayload(appMethod f offset (Values.operandIndex declared isNewObj spec.Argument), "url")
            | _ when spec.Role = "app-request" ->
                // A request made for such a call (Dapr's CreateInvokeMethodRequest), read where it is made.
                let made =
                    List.tryItem (Values.operandIndex declared isNewObj spec.Argument) (f.OperandsAt offset)
                    |> Option.map (stored f)
                    |> Option.bind (function
                        | [ DataFlow.Pushed o ] ->
                            match f.InstructionAt o with
                            | Some { Op = (Call c | CallVirt c) } ->
                                ctx.Matcher.Match c
                                |> List.tryPick (fun r -> r.Payload |> List.tryFind (fun p -> p.Role = "app-method"))
                                |> Option.map (fun p -> appMethod f o (Values.operandIndex c false p.Argument))
                            | _ -> None
                        | _ -> None)

                Values.Evaluator.ToPayload(defaultArg made Values.Hole, "url")
            | Some property when spec.Argument >= 0 ->
                let index = Values.operandIndex declared isNewObj spec.Argument
                Values.Evaluator.ToPayload(evaluator.OperandMember(offset, index, property, spec.ConstructorArgument), spec.Role)
            | Some property when declared.HasThis && not isNewObj ->
                evaluator.PropertyPayload(offset, property, spec.ConstructorArgument, spec.Role)
            | Some _ -> Values.Evaluator.ToPayload(Values.Hole, spec.Role)
            | None -> evaluator.Payload(offset, Values.operandIndex declared isNewObj spec.Argument, spec.Role))

    // Code a generator wrote into the application ([GeneratedCode] on the method, as Grpc.Tools' clients, or on its
    // type or an enclosing one, as Orleans's proxies and invokers): a call to a primitive in it is reported at the
    // application's call into it, which is where the effect is decided.
    let generatedAttribute (attributes: AttributeDef list) =
        attributes |> List.exists (fun a -> a.Type.FullName = "System.CodeDom.Compiler.GeneratedCodeAttribute")

    let rec generatedType (depth: int) (t: TypeRef) =
        depth < 8
        && (match ctx.Set.TryType t with
            | Some(def, _) -> generatedAttribute def.Attributes || (def.DeclaringType |> Option.exists (generatedType (depth + 1)))
            | None -> false)

    let isGenerated (d: MethodDef) =
        generatedAttribute d.Attributes || generatedType 0 d.Ref.Type

    let generatedReach = Dictionary<string, (SinkHit * Payload option * MethodRef list) list>()

    // The primitives generated code calls, following its calls to more generated code: each with its payload and the
    // generated methods on the way.
    let rec generatedSinks (depth: int) (target: MethodRef) : (SinkHit * Payload option * MethodRef list) list =
        match generatedReach.TryGetValue target.Key with
        | true, r -> r
        | _ when depth > 4 || not (ctx.Set.IsUser target.Type.Assembly) -> []
        | _ ->
            generatedReach[target.Key] <- []

            let r =
                match ctx.Set.TryMethod target with
                | Some(def, _) when isGenerated def ->
                    match def.Body.Value, DataFlow.ofMethod def with
                    | Some body, Some f ->
                        callSites ctx def body
                        |> Array.toList
                        |> List.collect (fun (_, ins, declared, edges) ->
                            let isNewObj = match ins.Op with NewObj _ -> true | _ -> false

                            edges
                            |> List.collect (fun e ->
                                match sinkOf ctx.Set ctx.Matcher e with
                                | Some hit -> [ hit, sinkPayload f ins.Offset declared isNewObj hit, [ target ] ]
                                | None -> generatedSinks (depth + 1) e.Target |> List.map (fun (h, p, chain) -> h, p, target :: chain)))
                        |> List.distinctBy (fun (h, p, _) -> h.Rule.Id, h.Primitive.Key, p)
                    | _ -> []
                | _ -> []

            generatedReach[target.Key] <- r
            r

    // Typed HTTP clients: a registration whose rule has a "typed-http-client" payload (AddHttpClient<TClient>(c =>
    // c.BaseAddress = new Uri(...))) gives the client type a base address, set by the configuration delegate it is
    // handed. A relative URL the client sends through the HttpClient it keeps in a field is relative to it.
    let baseAddresses =
        lazy
            (let delegateTarget (f: DataFlow.Flow) (values: DataFlow.Values) =
                values
                |> Set.toList
                |> List.tryPick (function
                    | DataFlow.Pushed o ->
                        match f.InstructionAt o with
                        | Some { Op = NewObj _ } ->
                            f.OperandsAt o
                            |> List.tryPick (fun vs ->
                                vs
                                |> Set.toList
                                |> List.tryPick (function
                                    | DataFlow.Pushed fo ->
                                        match f.InstructionAt fo with
                                        | Some { Op = (LdFtn m | LdVirtFtn m) } -> Some m
                                        | _ -> None
                                    | _ -> None))
                        | _ -> None
                    | _ -> None)

             // The constant a configuration method sets the client's BaseAddress to, by the catalog's rule for it.
             let baseAddressIn (configure: MethodRef) =
                 match ctx.Set.TryMethod configure with
                 | Some(def, _) ->
                     match def.Body.Value, DataFlow.ofMethod def with
                     | Some body, Some f ->
                         body.Instructions
                         |> Array.tryPick (fun ins ->
                             match ins.Op with
                             | CallVirt setter
                             | Call setter when
                                 ctx.Matcher.Match setter |> List.exists (fun r -> r.Payload |> List.exists (fun p -> p.Role = "base-address"))
                                 ->
                                 match (evaluatorOf f).Operand(ins.Offset, 1) with
                                 | Values.Text t -> Some t
                                 | _ -> None
                             | _ -> None)
                     | _ -> None
                 | None -> None

             assembly.Types
             |> List.collect (fun t -> t.Methods)
             |> List.collect (fun m ->
                 match m.Body.Value with
                 | None -> []
                 | Some body ->
                     body.Instructions
                     |> Array.toList
                     |> List.choose (fun ins ->
                         match ins.Op with
                         | Call d
                         | CallVirt d ->
                             let roles =
                                 ctx.Matcher.Match d
                                 |> List.filter (fun r -> r.Kind = Registration)
                                 |> List.collect (fun r -> r.Payload)

                             match DataFlow.ofMethod m with
                             | Some f when roles |> List.exists (fun p -> p.Role = "typed-http-client" || p.Role = "named-http-client") ->
                                 let configured () =
                                     f.OperandsAt ins.Offset |> List.tryPick (delegateTarget f) |> Option.bind baseAddressIn

                                 match roles |> List.tryFind (fun p -> p.Role = "typed-http-client"), roles |> List.tryFind (fun p -> p.Role = "named-http-client") with
                                 | Some spec, _ when spec.Argument < d.TypeArguments.Length ->
                                     configured () |> Option.map (fun b -> "type:" + d.TypeArguments[spec.Argument].FullName, b)
                                 | _, Some spec ->
                                     // A named client: AddHttpClient("github", c => ...), the name a constant.
                                     match (evaluatorOf f).Operand(ins.Offset, Values.operandIndex d false spec.Argument) with
                                     | Values.Text name -> configured () |> Option.map (fun b -> "name:" + name, b)
                                     | _ -> None
                                 | _ -> None
                             | _ -> None
                         | _ -> None))
             |> dict)

    // The client a call is made through: a typed client's HttpClient field ("type:"), one a factory makes by a
    // constant name ("name:", IHttpClientFactory.CreateClient("github")), or one that invokes another application
    // through a sidecar ("app:", Dapr's DaprClient.CreateInvokeHttpClient("billing")).
    let clientOf (m: MethodDef) (f: DataFlow.Flow) (offset: int) =
        match f.OperandsAt offset with
        | receiver :: _ ->
            stored f receiver
            |> List.tryPick (function
                | DataFlow.Pushed o ->
                    match f.InstructionAt o, f.OperandsAt o with
                    | Some { Op = LdFld fld }, [ owner ] when owner.Contains(DataFlow.Argument 0) && fld.Type.FullName = m.Ref.Type.FullName ->
                        Some("type:" + m.Ref.Type.FullName)
                    | Some { Op = (Call c | CallVirt c) }, _ when
                        ctx.Matcher.Match c |> List.exists (fun r -> r.Payload |> List.exists (fun p -> p.Role = "http-client-name"))
                        ->
                        let spec =
                            ctx.Matcher.Match c
                            |> List.pick (fun r -> r.Payload |> List.tryFind (fun p -> p.Role = "http-client-name"))

                        match (evaluatorOf f).Operand(o, Values.operandIndex c false spec.Argument) with
                        | Values.Text name -> Some("name:" + name)
                        | _ -> None
                    | Some { Op = (Call c | CallVirt c) }, _ when
                        ctx.Matcher.Match c |> List.exists (fun r -> r.Payload |> List.exists (fun p -> p.Role = "invoked-app"))
                        ->
                        let spec =
                            ctx.Matcher.Match c
                            |> List.pick (fun r -> r.Payload |> List.tryFind (fun p -> p.Role = "invoked-app"))

                        match (evaluatorOf f).Operand(o, Values.operandIndex c false spec.Argument) with
                        | Values.Text app -> Some("app:" + app)
                        | _ -> None
                    | _ -> None
                | _ -> None)
        | [] -> None

    // A relative URL sent through a typed or named client, with the client's base address before it.
    let withBaseAddress (m: MethodDef) (f: DataFlow.Flow) (offset: int) (payload: Payload option) =
        let baseOf (client: string) =
            if client.StartsWith "app:" then
                Some("http://" + client.Substring 4)
            else
                match baseAddresses.Value.TryGetValue client with
                | true, b -> Some b
                | _ -> None

        match payload, clientOf m f offset |> Option.bind (fun c -> baseOf c |> Option.map (fun b -> c, b)) with
        | Some p, Some(_, baseAddress) when p.Role = "url" ->
            let relative =
                match p.Value with
                | Some v when v.StartsWith "http://" || v.StartsWith "https://" -> None
                | Some v -> Some v
                | None -> if p.Kind = PayloadKind.Dynamic then Some "{…}" else None

            match relative with
            | Some r ->
                let joined = baseAddress.TrimEnd('/') + "/" + r.TrimStart('/')

                Some
                    { p with
                        Value = Some joined
                        Kind = if joined.Contains "{…}" then PayloadKind.Template else p.Kind }
            | _ -> payload
        | _ -> payload

    for t in assembly.Types do
        let implementsMarked = proxyMethodsOf t

        for m in t.Methods do
            match m.Body.Value with
            | None -> ()
            | Some _ when implementsMarked.ContainsKey m.Ref.Key -> ()
            | Some body ->
                let flow = lazy (DataFlow.ofMethod m)
                let generated = isGenerated m

                // A generated log method's own log call is not one: the call to the method is (generatedLogger).
                let generatedBody = (loggerAttribute ctx m.Attributes).IsSome

                let delegateLogger (offset: int) (declared: MethodRef) =
                    flow.Value |> Option.bind (fun f -> invokedLogger f offset declared)
                let seen = HashSet<int * EffectCategory>()

                let sites =
                    callSites ctx m body
                    |> Array.filter (fun (_, ins, declared, _) ->
                        not (generatedBody && ((ctx.Matcher.Logger declared).IsSome || (delegateLogger ins.Offset declared).IsSome)))

                for (index, ins, declared, edges) in sites do
                    let point = Instruction.pointAt body.Instructions index
                    let isNewObj = match ins.Op with NewObj _ -> true | _ -> false
                    // The logger rule, and the member shown as logging: the one called, or for a delegate the member
                    // that made it.
                    let logger, through =
                        match ctx.Matcher.Logger declared with
                        | Some r -> Some r, declared
                        | None ->
                            match generatedLogger ctx declared with
                            | Some r -> Some r, declared
                            | None ->
                                match delegateLogger ins.Offset declared with
                                | Some(r, maker) -> Some r, maker
                                | None -> None, declared

                    let isLogCall = logger.IsSome

                    let unit = lazy (ctx.Attributor.Attribute(projectId, m.Ref, point))

                    // An exit point at this call; `connection` is the one a dependency opens for it (opened).
                    let addOpening (connection: string option) (category: EffectCategory) (ruleId: string) (primitive: string) (via: string option) (payload: Payload option) (confidence: Confidence) (steps: EvidenceStep list) =
                        if seen.Add((ins.Offset, category)) then
                            match unit.Value with
                            | Some u ->
                                let location = point |> Option.map Attribution.toLocation

                                found.Add
                                    { ExitPoint =
                                        { Id = ""
                                          Category = category
                                          Unit = u.Id
                                          Member = m.Ref.Display
                                          Location = location
                                          RuleId = ruleId
                                          Primitive = primitive
                                          Payload = payload
                                          ViaAssembly = via
                                          Context =
                                              if category = EffectCategory.Database then
                                                  efContexts.At(m, flow.Value, ins.Offset, declared)
                                              else
                                                  None
                                          Connection = connection
                                          Confidence = confidence
                                          Evidence = { Steps = steps } }
                                      SortKey = sortKey u.Id location ins.Offset category ruleId }
                            | None -> unattributed category m point

                    let add = addOpening None

                    // Blind spots: the analysis cannot follow these calls.
                    let knownDynamic (rule: Rule) =
                        rule.BlindSpot = Some "dynamic"
                        && match flow.Value with
                           | Some f -> DynamicSites.isMvcViewData ctx.Set m f declared ins.Offset
                           | None -> false

                    match ctx.Matcher.BlindSpot declared with
                    | Some rule when not (knownDynamic rule) ->
                        let kind = defaultArg rule.BlindSpot "unknown"

                        diagnostics.Add
                            { Severity = DiagnosticSeverity.Warning
                              Code = "blind-spot." + kind.Replace(' ', '-')
                              Message = $"{m.Ref.Display} calls {declared.Display} ({kind}). What happens behind it is not analyzed."
                              ProjectId = Some projectId
                              Location = point |> Option.map Attribution.toLocation }
                    | _ -> ()

                    // A log call: grouped with the unit's other log calls that reach the same effect.
                    let logged (logger: MethodRef) (via: string) (category: EffectCategory) (d: DependencyEffect) (payload: Payload option) =
                        match unit.Value with
                        | Some u ->
                            let site =
                                { Caller = m.Ref
                                  Offset = ins.Offset
                                  Point = point
                                  Logger = logger }

                            let key = (u.Id, category, d.RuleId, d.Primitive, payload, via)

                            match logGroups.TryGetValue key with
                            | true, g ->
                                if not (g.Sites |> Seq.exists (fun s -> s.Caller.Key = m.Ref.Key && s.Offset = ins.Offset)) then
                                    g.Sites.Add site

                                if d.Steps.Length < g.Effect.Steps.Length then
                                    g.Effect <- d
                                    g.EffectSite <- site
                            | _ ->
                                logGroups[key] <-
                                    { Unit = u.Id
                                      Category = category
                                      Payload = payload
                                      ViaAssembly = via
                                      Sites = ResizeArray [ site ]
                                      Effect = d
                                      EffectSite = site }
                        | None -> if seen.Add((ins.Offset, category)) then unattributed category m point

                    // The library hands the call on to the loggers its providers make: what they reach, whatever the
                    // call's arguments, so a payload keeps only what is fixed in the logger's code.
                    let dispatched =
                        match logger |> Option.bind (fun r -> r.Dispatch) with
                        | Some callback ->
                            ctx.Dispatch callback
                            |> List.groupBy (fun (via, d) -> via, d.Category)
                            |> List.iter (fun ((via, category), ds) ->
                                // As through a dependency: the shortest route whose payload is known, else the shortest.
                                let resolved = ds |> List.map (fun (_, d) -> d, fixedPayload d)
                                let shortest = List.minBy (fun (d: DependencyEffect, _) -> d.Steps.Length, d.RuleId)

                                let d, payload =
                                    match preferred resolved with
                                    | [] -> shortest resolved
                                    | known -> shortest known

                                logged through via category d payload)

                            true
                        | None -> false

                    // A call that enumerates a query runs it on the database (Entity Framework Core's DbSet<T>).
                    let ran =
                        if isLogCall then
                            None
                        else
                            flow.Value
                            |> Option.bind (fun f -> Queries.enumerated queries f m ins.Offset declared isNewObj)

                    match ran with
                    | Some q ->
                        add
                            q.Rule.Category.Value
                            q.Rule.Id
                            declared.Display
                            None
                            (queryPayload q)
                            Confidence.High
                            [ step $"{m.Ref.Display} calls {declared.Display}" m.Ref point
                              step $"{declared.Display} enumerates a query, from {q.Description}, which runs it on the database (rule {q.Rule.Id})" declared None ]
                    | None ->
                        // A call that passes a query to a method that enumerates it runs the query: an exit point
                        // here, at each such call, as the method has no query of its own.
                        let passed =
                            if isLogCall then
                                None
                            else
                                flow.Value
                                |> Option.bind (fun f ->
                                    let operands = f.OperandsAt ins.Offset

                                    edges
                                    |> List.map (fun e -> e.Target)
                                    |> List.distinctBy (fun t -> t.Key)
                                    |> List.tryPick (fun target ->
                                        enumeratedParameters target
                                        |> List.tryPick (fun p ->
                                            List.tryItem (if isNewObj then p.Argument - 1 else p.Argument) operands
                                            |> Option.bind (Queries.ofValues queries f m)
                                            |> Option.map (fun q -> target, p, q))))

                        match passed with
                        | Some(target, p, q) ->
                            let via =
                                match ctx.Set.Origin target.Type.Assembly with
                                | Dependency -> Some target.Type.Assembly
                                | _ -> None

                            let calledName = p.Path.Head.Name
                            let last = List.last p.Path
                            let enumerating = last.Method
                            let held (h: Queries.Hop) = if h.InField then $"field {h.Name}" else $"parameter {h.Name}"

                            let passedOn =
                                List.pairwise p.Path
                                |> List.map (fun (from, into) ->
                                    step $"{from.Method.Display} passes it to {into.Method.Display} as {held into}" from.Method None)

                            add
                                q.Rule.Category.Value
                                q.Rule.Id
                                p.Enumerator.Display
                                via
                                (queryPayload q)
                                Confidence.High
                                [ step $"{m.Ref.Display} calls {target.Display}, passing a query from {q.Description} as parameter {calledName}" m.Ref point
                                  yield! passedOn
                                  step $"{enumerating.Display} calls {p.Enumerator.Display}" enumerating None
                                  step $"{p.Enumerator.Display} enumerates {held last} of {enumerating.Display}, which runs the query on the database (rule {q.Rule.Id})" p.Enumerator None ]
                        | None -> ()

                    // A call to a method an attribute marks as the effect (Refit's [Get("/users/{id}")]), or to a
                    // proxy's implementation of one: the attribute's argument is the payload, or for a WCF operation
                    // its action.
                    if not isLogCall && ctx.Set.Origin declared.Type.Assembly <> Framework then
                        match markedTarget declared with
                        | Some def ->
                            def.Attributes
                            |> List.tryPick (fun a -> ctx.Matcher.MarkingSink a.Type |> Option.map (fun r -> a, r))
                            |> Option.iter (fun (a, rule) ->
                                let payload =
                                    rule.Payload
                                    |> List.tryHead
                                    |> Option.map (fun spec ->
                                        match spec.Role with
                                        | "wcf-action" ->
                                            match Wcf.actionOf ctx.Set ctx.Matcher def with
                                            | Some action -> Values.Evaluator.ToPayload(Values.Text action, spec.Role)
                                            | None -> Values.Evaluator.ToPayload(Values.Hole, spec.Role)
                                        | _ ->
                                            match List.tryItem spec.Argument a.Arguments with
                                            | Some(AttributeValue.Text t) -> Values.Evaluator.ToPayload(Values.Text t, spec.Role)
                                            | _ -> Values.Evaluator.ToPayload(Values.Hole, spec.Role))

                                let through =
                                    if def.Ref.Key = declared.Key then []
                                    else [ step $"{declared.Display} implements {def.Ref.Display} and passes the call on to it" declared None ]

                                add
                                    rule.Category.Value
                                    rule.Id
                                    def.Ref.Display
                                    None
                                    payload
                                    Confidence.High
                                    [ step $"{m.Ref.Display} calls {declared.Display}" m.Ref point
                                      yield! through
                                      step $"{def.Ref.Display} is marked {a.Type.FullName}, which makes a call to it a {categoryName rule.Category.Value} call (rule {rule.Id})" def.Ref None ])
                        | None -> ()

                    for edge in edges |> List.filter (fun _ -> not dispatched) do
                        match sinkOf ctx.Set ctx.Matcher edge with
                        | Some hit ->
                            // A direct call to a primitive. Without a payload of its own, a call that runs a query
                            // (ToListAsync) shows the SQL the query was made from.
                            let ofQuery () =
                                flow.Value
                                |> Option.bind (fun f ->
                                    Queries.ofFirstOperand queries f m ins.Offset
                                    |> Option.filter (fun q -> q.Rule.Category = Some hit.Category)
                                    |> Option.bind queryPayload)

                            let payload =
                                flow.Value
                                |> Option.bind (fun f -> sinkPayload f ins.Offset declared isNewObj hit |> withBaseAddress m f ins.Offset)
                                |> Option.orElseWith (fun () -> if hit.Rule.Payload.IsEmpty then ofQuery () else None)

                            // In generated code, the call that reaches it reports it (generatedSinks).
                            if not generated then
                                add
                                    hit.Category
                                    hit.Rule.Id
                                    hit.Primitive.Display
                                    None
                                    payload
                                    Confidence.High
                                    [ step $"{m.Ref.Display} calls {hit.Primitive.Display}" m.Ref point
                                      step $"{hit.Primitive.Display} is a {categoryName hit.Category} primitive (rule {hit.Rule.Id})" hit.Primitive None ]
                        | None when not generated && not isLogCall && (generatedSinks 0 edge.Target).Length > 0 ->
                            // A call into generated code (a gRPC client): what its calls to primitives do, here.
                            for (hit, payload, chain) in generatedSinks 0 edge.Target do
                                let through =
                                    List.pairwise chain
                                    |> List.map (fun (a: MethodRef, b: MethodRef) -> step $"{a.Display} calls {b.Display}, which is generated code" a None)

                                add
                                    hit.Category
                                    hit.Rule.Id
                                    hit.Primitive.Display
                                    None
                                    payload
                                    Confidence.High
                                    ([ step $"{m.Ref.Display} calls {chain.Head.Display}, which is generated code" m.Ref point ]
                                     @ through
                                     @ [ step $"{(List.last chain).Display} calls {hit.Primitive.Display}" (List.last chain) None
                                         step $"{hit.Primitive.Display} is a {categoryName hit.Category} primitive (rule {hit.Rule.Id})" hit.Primitive None ])
                        | None ->
                            match ctx.Set.Origin edge.Target.Type.Assembly with
                            | Dependency ->
                                // Through a dependency: one exit point per category it can reach. The path is the
                                // shortest one whose payload, worked out through the dependency's code, has a
                                // value here; failing that, the shortest one.
                                let payloadOf (d: DependencyEffect) =
                                    d.Payload
                                    |> Option.map (fun p ->
                                        match flow.Value with
                                        | Some f ->
                                            let evaluator = evaluatorOf f
                                            Values.Evaluator.ToPayload(evaluator.Resolve(p.Origin, ins.Offset, isNewObj), p.Role)
                                        | None -> Values.Evaluator.ToPayload(Values.Hole, p.Role))

                                ctx.Effects edge.Target
                                |> List.groupBy (fun d -> d.Category)
                                |> List.iter (fun (category, ds) ->
                                    let resolved = ds |> List.map (fun d -> d, payloadOf d)
                                    let shortest = List.minBy (fun (d: DependencyEffect, _) -> d.Steps.Length, d.RuleId)

                                    let d, payload =
                                        match preferred resolved with
                                        | [] -> shortest resolved
                                        | known -> shortest known

                                    let via = edge.Target.Type.Assembly

                                    if isLogCall then
                                        logged edge.Target via category d payload
                                    else
                                        let steps = step $"{m.Ref.Display} calls {edge.Target.Display}" m.Ref point :: dependencySteps d category
                                        // How sure it is that the call has the effect: its most direct route, even
                                        // when a longer one is shown for its payload.
                                        let confidence = ds |> List.map (fun d -> min d.Direct (scoreOf d.Steps)) |> List.min |> confidenceOfScore
                                        addOpening (opened resolved payload) category d.RuleId d.Primitive (Some via) payload confidence steps)
                            | _ -> ()

                    // A call that loads a configuration file makes the objects it names: what their constructors
                    // (or the methods called by name) do happens here, after what the call itself reaches. Their
                    // arguments come from the file, so a payload keeps only what is fixed in the library's code.
                    if not isLogCall then
                        for (named, maker) in ctx.Loaded declared do
                            ctx.Effects maker
                            |> List.groupBy (fun d -> d.Category)
                            |> List.iter (fun (category, ds) ->
                                let resolved = ds |> List.map (fun d -> d, fixedPayload d)
                                let shortest = List.minBy (fun (d: DependencyEffect, _) -> d.Steps.Length, d.RuleId)

                                let d, payload =
                                    match preferred resolved with
                                    | [] -> shortest resolved
                                    | known -> shortest known

                                let steps =
                                    [ step $"{m.Ref.Display} calls {declared.Display}" m.Ref point
                                      { Description = $"{declared.Display} loads the configuration: {named}, made by {maker.Display}"
                                        Member = maker.Display
                                        Assembly = maker.Type.Assembly
                                        Location = None } ]
                                    @ dependencySteps d category

                                add category d.RuleId d.Primitive (Some maker.Type.Assembly) payload (confidenceOf d.Steps) steps)

    for g in logGroups.Values do
        let location (s: LogSite) = s.Point |> Option.map Attribution.toLocation

        let sites =
            g.Sites
            |> Seq.sortBy (fun s ->
                match location s with
                | Some l -> l.File, l.StartLine, s.Offset
                | None -> "", 0, s.Offset)
            |> Seq.toList

        let first = sites.Head
        let d = g.Effect
        let calls = if sites.Length = 1 then "1 log call" else $"{sites.Length} log calls"

        let steps =
            (sites |> List.map (fun s -> step $"{s.Caller.Display} logs through {s.Logger.Display}" s.Caller s.Point))
            @ [ { Description =
                    $"Every log call reaches what the logger is configured with ({calls} in this unit); the shortest route, from {g.EffectSite.Logger.Display}:"
                  Member = g.EffectSite.Logger.Display
                  Assembly = g.ViaAssembly
                  Location = None } ]
            @ dependencySteps d g.Category

        found.Add
            { ExitPoint =
                { Id = ""
                  Category = g.Category
                  Unit = g.Unit
                  Member = first.Caller.Display
                  Location = location first
                  RuleId = d.RuleId
                  Primitive = d.Primitive
                  Payload = g.Payload
                  ViaAssembly = Some g.ViaAssembly
                  Context = None
                  Connection = None
                  Confidence = confidenceOf d.Steps
                  Evidence = { Steps = steps } }
              SortKey = sortKey g.Unit (location first) first.Offset g.Category d.RuleId }

    List.ofSeq found, List.ofSeq diagnostics
