/// Entry-to-exit traces (plan section 6.8): the side-effects each entry point can cause, by reachability from its
/// handler through user code. Exit points are call sites in user code, so following user code is enough.
module Floorplan.Analysis.Traces

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL

let compute (program: EffectAnalysis.Program) (entries: EntryPoint list) (exits: ExitPoint list) : Trace list =
    let set = program.Set

    let methodsByDisplay =
        program.Users
        |> Seq.collect (fun (_, a) -> a.Types)
        |> Seq.collect (fun t -> t.Methods)
        |> Seq.groupBy (fun m -> m.Ref.Display)
        |> Seq.map (fun (d, ms) -> d, ms |> Seq.map (fun m -> m.Ref) |> Seq.toList)
        |> dict

    // The user members whose calls are the exit point: its own, and for the log calls grouped into one exit point
    // (ExitPoints), every member the evidence names in user code.
    let exitsByMember =
        exits
        |> List.collect (fun x ->
            x.Member :: [ for s in x.Evidence.Steps do if set.IsUser s.Assembly then yield s.Member ]
            |> List.distinct
            |> List.map (fun m -> m, x))
        |> List.groupBy fst
        |> List.map (fun (m, xs) -> m, List.map snd xs)
        |> dict
    let cache = Dictionary<string, Set<string>>()

    /// Members of user methods reachable from a method, itself included.
    let reach (start: MethodRef) =
        match cache.TryGetValue start.Key with
        | true, r -> r
        | _ ->
            let seen = HashSet<string>()
            let members = HashSet<string>()
            let stack = Stack<MethodRef>([ start ])

            while stack.Count > 0 do
                let m = stack.Pop()

                if seen.Add m.Key then
                    members.Add m.Display |> ignore

                    for e in program.Graph.Callees m do
                        if e.Kind <> Initializer && set.IsUser e.Target.Type.Assembly && not (seen.Contains e.Target.Key) then
                            stack.Push e.Target

            let r = Set.ofSeq members
            cache[start.Key] <- r
            r

    entries
    |> List.map (fun e ->
        let starts =
            match methodsByDisplay.TryGetValue e.HandlerMember with
            | true, ms -> ms
            | _ -> []

        let reached = starts |> List.map reach |> Set.unionMany

        let ids =
            reached
            |> Seq.collect (fun m ->
                match exitsByMember.TryGetValue m with
                | true, xs -> xs |> Seq.map (fun x -> x.Id)
                | _ -> Seq.empty)
            |> Seq.distinct
            |> Seq.sort
            |> Seq.toList

        { EntryPointId = e.Id; ExitPointIds = ids })
