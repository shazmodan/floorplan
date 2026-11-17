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
      Attributor: Attribution.Attributor
      Recognizers: ICodegenRecognizer list
      /// What constructors and other methods set on the objects they produce, read from their bodies.
      Producers: Values.Producers }

/// An exit point before ids are assigned.
type Found =
    { ExitPoint: ExitPoint
      SortKey: string * string * int * int * string }

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

let analyze (ctx: ProjectContext) (assembly: AssemblyDef) : Found list * Diagnostic list =
    let found = ResizeArray<Found>()

    let evaluatorOf (flow: DataFlow.Flow) =
        Values.Evaluator(flow, ctx.Recognizers, Values.constantReturn ctx.Set, fun m n -> ctx.Producers.Member(m, n))
    let diagnostics = ResizeArray<Diagnostic>()
    let projectId = ctx.Project.ProjectId

    for t in assembly.Types do
        for m in t.Methods do
            match m.Body.Value with
            | None -> ()
            | Some body ->
                let flow = lazy (DataFlow.ofMethod m)
                let seen = HashSet<int * EffectCategory>()

                for (index, ins, declared, edges) in callSites ctx m body do
                    let point = Instruction.pointAt body.Instructions index
                    let isNewObj = match ins.Op with NewObj _ -> true | _ -> false

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
                                      SortKey =
                                        (u.Id,
                                         (match location with Some l -> l.File | None -> ""),
                                         (match location with Some l -> l.StartLine | None -> 0),
                                         ins.Offset,
                                         categoryName category + ruleId) }
                            | None ->
                                diagnostics.Add
                                    { Severity = DiagnosticSeverity.Warning
                                      Code = "exit-point.unattributed"
                                      Message = $"A {categoryName category} call in {m.Ref.Display} could not be attributed to a unit."
                                      ProjectId = Some projectId
                                      Location = point |> Option.map Attribution.toLocation }

                    // Blind spots: the analysis cannot follow these calls.
                    match ctx.Matcher.BlindSpot declared with
                    | Some rule ->
                        let kind = defaultArg rule.BlindSpot "unknown"

                        diagnostics.Add
                            { Severity = DiagnosticSeverity.Warning
                              Code = "blind-spot." + kind.Replace(' ', '-')
                              Message = $"{m.Ref.Display} calls {declared.Display} ({kind}). What happens behind it is not analyzed."
                              ProjectId = Some projectId
                              Location = point |> Option.map Attribution.toLocation }
                    | None -> ()

                    for edge in edges do
                        match sinkOf ctx.Set ctx.Matcher edge with
                        | Some hit ->
                            // A direct call to a primitive.
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

                                    let steps =
                                        [ step $"{m.Ref.Display} calls {edge.Target.Display}" m.Ref point
                                          yield!
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

                                    add category d.RuleId d.Primitive (Some edge.Target.Type.Assembly) payload (confidenceOf d.Steps) steps)
                            | _ -> ()

    List.ofSeq found, List.ofSeq diagnostics
