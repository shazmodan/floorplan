/// Matches call targets against catalog rules, including overrides and implementations in derived types.
namespace Floorplan.Catalog

open System.Collections.Generic
open Floorplan.IL

type Matcher(catalog: Catalog, supertypes: TypeRef -> Set<string>) =
    let exact =
        catalog.Rules |> List.groupBy (fun r -> r.Type) |> dict

    let derived =
        catalog.Rules
        |> List.filter (fun r -> r.IncludeDerivedTypes)
        |> List.groupBy (fun r -> r.Type)
        |> dict

    let cache = Dictionary<string, Rule list>()

    static member MemberMatches (pattern: string) (name: string) =
        if pattern = "*" then true
        elif pattern.EndsWith "*" then name.StartsWith(pattern.TrimEnd '*')
        else
            // Explicit interface implementations are named "Namespace.IFace.Member".
            name = pattern || name.EndsWith("." + pattern)

    static member RuleApplies (rule: Rule) (m: MethodRef) =
        rule.Members |> List.exists (fun p -> Matcher.MemberMatches p m.Name)
        && (match rule.WhenFirstParameter with
            | None -> true
            | Some t -> (match m.ParameterTypes with first :: _ -> first = t | [] -> false))

    member _.Catalog = catalog

    /// Every rule that applies to a call of `m`, rules on the exact type first.
    member _.Match(m: MethodRef) : Rule list =
        match cache.TryGetValue m.Key with
        | true, r -> r
        | _ ->
            let onType =
                match exact.TryGetValue m.Type.FullName with
                | true, rules -> rules |> List.filter (fun r -> Matcher.RuleApplies r m)
                | _ -> []

            let inherited =
                if derived.Count = 0 then
                    []
                else
                    supertypes m.Type
                    |> Seq.filter (fun s -> s <> m.Type.FullName)
                    |> Seq.collect (fun s ->
                        match derived.TryGetValue s with
                        | true, rules -> rules |> List.filter (fun r -> Matcher.RuleApplies r m)
                        | _ -> [])
                    |> Seq.toList

            let result = (onType @ inherited) |> List.distinctBy (fun r -> r.Id)
            cache[m.Key] <- result
            result

    member this.Sink(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Sink)

    member this.Source(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Source)

    member this.Registration(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Registration)

    member this.BlindSpot(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = BlindSpot)
