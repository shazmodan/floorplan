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
      Attributor: Attribution.Attributor
      Recognizers: ICodegenRecognizer list
      /// What constructors and other methods set on the objects they produce, read from their bodies.
      Producers: Values.Producers }

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

/// Confidence from the evidence path through a dependency: each call is one step, each virtual call one more,
/// because virtual dispatch is where a static call graph over-approximates.
let confidenceOf (path: PathStep list) =
    let score = path.Length + (path |> List.filter (fun e -> e.Virtual) |> List.length)

    if score <= 4 then Confidence.High
    elif score <= 12 then Confidence.Medium
    else Confidence.Low

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

    let evaluatorOf (flow: DataFlow.Flow) =
        Values.Evaluator(flow, ctx.Recognizers, Values.constantReturn ctx.Set, fun m n -> ctx.Producers.Member(m, n))
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
                |> List.map (fun e -> e.Target) }

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

    for t in assembly.Types do
        for m in t.Methods do
            match m.Body.Value with
            | None -> ()
            | Some body ->
                let flow = lazy (DataFlow.ofMethod m)

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

                    let add (category: EffectCategory) (ruleId: string) (primitive: string) (via: string option) (payload: Payload option) (confidence: Confidence) (steps: EvidenceStep list) =
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
                                          Confidence = confidence
                                          Evidence = { Steps = steps } }
                                      SortKey = sortKey u.Id location ins.Offset category ruleId }
                            | None -> unattributed category m point

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
                                let payloadOf (d: DependencyEffect) =
                                    d.Payload
                                    |> Option.map (fun p ->
                                        let parts =
                                            p.Origin
                                            |> List.map (function
                                                | Values.Literal t -> Values.Text t
                                                | _ -> Values.Hole)

                                        Values.Evaluator.ToPayload(Values.Parts parts, p.Role))

                                // As through a dependency: the shortest route whose payload is known, else the shortest.
                                let resolved = ds |> List.map (fun (_, d) -> d, payloadOf d)
                                let shortest = List.minBy (fun (d: DependencyEffect, _) -> d.Steps.Length, d.RuleId)

                                let d, payload =
                                    match resolved |> List.filter (fun (_, p) -> p |> Option.exists (fun p -> p.Kind <> PayloadKind.Dynamic)) with
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
                                hit.Rule.Payload
                                |> List.tryHead
                                |> Option.bind (fun spec ->
                                    flow.Value
                                    |> Option.map (fun f ->
                                        let evaluator = evaluatorOf f

                                        match spec.Property with
                                        | Some property when spec.Argument >= 0 ->
                                            let index = Values.operandIndex declared isNewObj spec.Argument
                                            Values.Evaluator.ToPayload(evaluator.OperandMember(ins.Offset, index, property, spec.ConstructorArgument), spec.Role)
                                        | Some property when declared.HasThis && not isNewObj ->
                                            evaluator.PropertyPayload(ins.Offset, property, spec.ConstructorArgument, spec.Role)
                                        | Some _ -> Values.Evaluator.ToPayload(Values.Hole, spec.Role)
                                        | None -> evaluator.Payload(ins.Offset, Values.operandIndex declared isNewObj spec.Argument, spec.Role)))
                                |> Option.orElseWith (fun () -> if hit.Rule.Payload.IsEmpty then ofQuery () else None)

                            add
                                hit.Category
                                hit.Rule.Id
                                hit.Primitive.Display
                                None
                                payload
                                Confidence.High
                                [ step $"{m.Ref.Display} calls {hit.Primitive.Display}" m.Ref point
                                  step $"{hit.Primitive.Display} is a {categoryName hit.Category} primitive (rule {hit.Rule.Id})" hit.Primitive None ]
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
                                        match resolved |> List.filter (fun (_, p) -> p |> Option.exists (fun p -> p.Kind <> PayloadKind.Dynamic)) with
                                        | [] -> shortest resolved
                                        | known -> shortest known

                                    let via = edge.Target.Type.Assembly

                                    if isLogCall then
                                        logged edge.Target via category d payload
                                    else
                                        let steps = step $"{m.Ref.Display} calls {edge.Target.Display}" m.Ref point :: dependencySteps d category
                                        add category d.RuleId d.Primitive (Some via) payload (confidenceOf d.Steps) steps)
                            | _ -> ()

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
                  Confidence = confidenceOf d.Steps
                  Evidence = { Steps = steps } }
              SortKey = sortKey g.Unit (location first) first.Offset g.Category d.RuleId }

    List.ofSeq found, List.ofSeq diagnostics
