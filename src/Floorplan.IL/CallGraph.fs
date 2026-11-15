/// The call graph over user assemblies and their dependencies, built by rapid type analysis: a virtual call
/// only reaches implementations in types that are actually instantiated in reachable code (plan section 5).
/// Calls into shared-framework assemblies are leaves: the boundary catalog decides what they mean.
namespace Floorplan.IL

open System.Collections.Generic

type EdgeKind =
    /// call, or callvirt on a non-virtual or sealed method.
    | Direct
    /// callvirt resolved to an implementation in an instantiated type.
    | Virtual
    /// A function object or delegate is created here; its body runs later.
    | Deferred
    /// An async state machine is started here; MoveNext runs later.
    | StateMachine

type CallEdge =
    { Caller: MethodRef
      /// Offset of the instruction in the caller.
      Offset: int
      /// The method named at the call site.
      Declared: MethodRef
      /// The method that runs: the declared one for direct calls, an implementation for virtual ones.
      Target: MethodRef
      Kind: EdgeKind }

type CallGraph(set: AssemblySet, recognizers: ICodegenRecognizer list) =
    let callees = Dictionary<string, ResizeArray<CallEdge>>()
    let reachable = HashSet<string>()
    let methodsByKey = Dictionary<string, MethodRef>()
    let instantiated = HashSet<TypeRef>()
    // Instantiated types by every type they are assignable to, and virtual call sites by declared type.
    let instantiatedBySuper = Dictionary<string, ResizeArray<TypeRef>>()
    let pendingBySuper = Dictionary<string, ResizeArray<MethodRef * MethodRef * int>>() // caller, declared, offset

    let bucket (d: Dictionary<string, ResizeArray<'T>>) (k: string) =
        match d.TryGetValue k with
        | true, l -> l
        | _ ->
            let l = ResizeArray()
            d[k] <- l
            l
    let work = Queue<MethodRef>()

    let recognizerFor =
        let cache = Dictionary<string, ICodegenRecognizer list>()

        fun (assembly: string) ->
            match cache.TryGetValue assembly with
            | true, r -> r
            | _ ->
                let r =
                    match set.TryLoad assembly with
                    | Some a -> recognizers |> List.filter (fun x -> x.AppliesTo a)
                    | None -> []

                cache[assembly] <- r
                r

    let analyzable (m: MethodRef) =
        match set.Origin m.Type.Assembly with
        | Framework -> false
        | _ -> true

    let addEdge (edge: CallEdge) =
        let list =
            match callees.TryGetValue edge.Caller.Key with
            | true, l -> l
            | _ ->
                let l = ResizeArray()
                callees[edge.Caller.Key] <- l
                l

        if not (list |> Seq.exists (fun e -> e.Offset = edge.Offset && e.Target.Key = edge.Target.Key)) then
            list.Add edge

        if analyzable edge.Target && reachable.Add edge.Target.Key then
            methodsByKey[edge.Target.Key] <- edge.Target
            work.Enqueue edge.Target

    let isFunctionInvoke (m: MethodRef) =
        CommonCodegen.isDelegateInvoke set m || recognizers |> List.exists (fun r -> r.IsFunctionInvoke m)

    let resolveVirtual (caller: MethodRef) (declared: MethodRef) (offset: int) (t: TypeRef) =
        match set.TryType t with
        | Some(def, _) when not def.IsInterface && not def.IsAbstract ->
            match set.Implementation(t, declared) with
            | Some impl ->
                addEdge
                    { Caller = caller
                      Offset = offset
                      Declared = declared
                      Target = impl
                      Kind = Virtual }
            | None -> ()
        | _ -> ()

    /// Construction links: function objects and async state machines run their bodies later.
    let deferredTargets (t: TypeRef) =
        match set.TryType t with
        | None -> []
        | Some(def, asm) ->
            let fromRecognizers =
                recognizerFor asm.Name |> List.collect (fun r -> r.FunctionObjectTargets(set, def))

            let stateMachine =
                if set.IsSubtypeOf(t, CommonCodegen.asyncStateMachine) then
                    def.Methods |> List.filter (fun m -> m.Ref.Name = "MoveNext" || m.Ref.Name.EndsWith ".MoveNext") |> List.map (fun m -> m.Ref)
                else
                    []

            (fromRecognizers |> List.map (fun m -> m, Deferred)) @ (stateMachine |> List.map (fun m -> m, StateMachine))

    let instantiate (t: TypeRef) =
        if instantiated.Add t then
            for super in set.Supertypes t do
                (bucket instantiatedBySuper super).Add t

                match pendingBySuper.TryGetValue super with
                | true, sites ->
                    for (caller, declared, offset) in List.ofSeq sites do
                        resolveVirtual caller declared offset t
                | _ -> ()

    let scan (m: MethodRef) =
        match set.TryMethod m with
        | None -> ()
        | Some(def, _) ->
            match def.Body.Value with
            | None -> ()
            | Some body ->
                let instrs = body.Instructions
                let linkDeferred (offset: int) (t: TypeRef) =
                    for (target, kind) in deferredTargets t do
                        addEdge
                            { Caller = m
                              Offset = offset
                              Declared = target
                              Target = target
                              Kind = kind }

                // Value-type state machines are locals, started through a framework builder.
                for local in body.LocalTypes |> List.distinct do
                    if set.Origin local.Assembly <> Framework then
                        linkDeferred 0 local

                for i in 0 .. instrs.Length - 1 do
                    let ins = instrs[i]

                    let direct (target: MethodRef) kind =
                        addEdge
                            { Caller = m
                              Offset = ins.Offset
                              Declared = target
                              Target = target
                              Kind = kind }

                    match ins.Op with
                    | Call target -> direct target Direct
                    | NewObj ctor ->
                        instantiate ctor.Type
                        direct ctor Direct
                        if analyzable ctor then linkDeferred ins.Offset ctor.Type
                    | InitObj t -> if set.Origin t.Assembly <> Framework then linkDeferred ins.Offset t
                    // A cached function object read from a static field, e.g. a capture-free closure instance.
                    | LdSFld f when set.Origin f.Type.Assembly <> Framework && f.FieldType.StartsWith f.Type.FullName ->
                        instantiate f.Type
                        linkDeferred ins.Offset f.Type
                    | LdFtn target
                    | LdVirtFtn target -> direct target Deferred
                    | CallVirt declared ->
                        let constrainedTo =
                            if i > 0 then
                                match instrs[i - 1].Op with
                                | Constrained t -> Some t
                                | _ -> None
                            else
                                None

                        match set.TryMethod declared with
                        | _ when not (analyzable declared) -> direct declared Direct
                        | _ when isFunctionInvoke declared -> direct declared Direct
                        | Some(d, owner) when not d.IsVirtual || d.IsFinal || owner.IsSealed -> direct declared Direct
                        | _ ->
                            match constrainedTo with
                            | Some t ->
                                match set.Implementation(t, declared) with
                                | Some impl -> direct impl Virtual
                                | None -> direct declared Direct
                            | None ->
                                // The declared method itself runs when it has a body and is not overridden.
                                match set.TryMethod declared with
                                | Some(d, _) when not d.IsAbstract -> direct declared Direct
                                | _ -> ()

                                (bucket pendingBySuper declared.Type.FullName).Add((m, declared, ins.Offset))

                                match instantiatedBySuper.TryGetValue declared.Type.FullName with
                                | true, types ->
                                    for t in List.ofSeq types do
                                        resolveVirtual m declared ins.Offset t
                                | _ -> ()
                    | _ -> ()

    let rec drain () =
        while work.Count > 0 do
            scan (work.Dequeue())

    /// Adds methods as roots and explores everything reachable from them.
    member _.AddRoots(roots: MethodRef seq) =
        for r in roots do
            if reachable.Add r.Key then
                methodsByKey[r.Key] <- r
                work.Enqueue r

        drain ()

    member _.Callees(m: MethodRef) : CallEdge list =
        match callees.TryGetValue m.Key with
        | true, l -> List.ofSeq l
        | _ -> []

    member _.IsReachable(m: MethodRef) = reachable.Contains m.Key

    member _.ReachableMethods = methodsByKey.Values |> Seq.toList

    member _.Instantiated = instantiated |> Seq.toList

    member _.Set = set
