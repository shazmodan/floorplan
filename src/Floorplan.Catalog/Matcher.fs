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

    let loggerAttributes =
        catalog.Rules
        |> List.filter (fun r -> r.Kind = Logger && r.LogCall = LogCall.Attribute)
        |> List.distinctBy (fun r -> r.Type)
        |> List.map (fun r -> r.Type, r)
        |> dict

    let queryTypes =
        catalog.Rules
        |> List.filter (fun r -> r.Kind = Query && r.Members = [ "*" ])
        |> List.distinctBy (fun r -> r.Type)
        |> List.map (fun r -> r.Type, r)
        |> dict

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
        && (rule.WhenParameters.Length <= m.ParameterTypes.Length
            && List.forall2 (fun (want: string) (have: string) -> want = have) rule.WhenParameters (List.truncate rule.WhenParameters.Length m.ParameterTypes))

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

    /// A logger rule for which a call of `m` is itself a log call.
    member this.Logger(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Logger && r.LogCall = LogCall.Direct)

    /// A logger rule whose members, such as `m`, make a delegate that logs when invoked (LoggerMessage.Define).
    member this.LoggerDelegate(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Logger && r.LogCall = LogCall.Delegate)

    /// A logger rule for an attribute that marks log methods ([LoggerMessage]), by the attribute's full name.
    member _.LoggerAttribute(fullName: string) : Rule option =
        match loggerAttributes.TryGetValue fullName with
        | true, r -> Some r
        | _ -> None

    /// A query rule whose results are queries, for a call of `m`.
    member this.Query(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Query)

    /// A query rule that makes every value of the type a query (members "*"), by the type's full name.
    member _.QueryType(fullName: string) : Rule option =
        match queryTypes.TryGetValue fullName with
        | true, r -> Some r
        | _ -> None

    /// A self-log rule for a call of `m`: a library's log of its own workings, off unless turned on.
    member this.SelfLog(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = SelfLog)

    member this.Comparison(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = RuleKind.Comparison)

    /// The value of a platform static field, when the catalog knows it.
    member _.Constant(f: FieldRef) : string option =
        catalog.Rules
        |> List.tryFind (fun r -> r.Kind = Constant && r.Type = f.Type.FullName && List.contains f.Name r.Members)
        |> Option.bind (fun r -> r.ComparesTo)

    /// The value a platform property getter is known to return, e.g. HttpResponse.HasStarted while routing.
    member _.ConstantGetter(m: MethodRef) : string option =
        catalog.Rules
        |> List.tryFind (fun r -> r.Kind = Constant && r.Type = m.Type.FullName && List.contains m.Name r.Members)
        |> Option.bind (fun r -> r.ComparesTo)

    member this.Metadata(m: MethodRef) =
        this.Match m |> List.tryFind (fun r -> r.Kind = Metadata)

    /// Callback rules on a type or its base types, for members an object of that type overrides.
    member _.Callbacks(t: TypeRef) : Rule list =
        let supers = supertypes t

        catalog.Rules
        |> List.filter (fun r -> r.Kind = Callback && supers.Contains r.Type)
