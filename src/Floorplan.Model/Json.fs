/// JSON for the analysis model: camelCase names, fieldless unions as kebab-case strings, options as null.
module Floorplan.Model.Json

open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.FSharp.Reflection

let private kebab (name: string) =
    let builder = StringBuilder()

    name
    |> Seq.iteri (fun i c ->
        if Char.IsUpper c && i > 0 then
            builder.Append('-') |> ignore

        builder.Append(Char.ToLowerInvariant c) |> ignore)

    builder.ToString()

/// Serialized spelling of a fieldless union case. A few cases have names that read badly in kebab case.
let caseName (case: UnionCaseInfo) =
    match case.DeclaringType.Name, case.Name with
    | "Language", "FSharp" -> "fsharp"
    | "Language", "CSharp" -> "csharp"
    | "Language", "OtherLanguage" -> "other"
    | "UnitKind", "ProjectUnit" -> "project"
    | _, name -> kebab name

/// The serialized spelling of any fieldless union value.
let unionName (value: 'T) =
    let case, _ = FSharpValue.GetUnionFields(value, typeof<'T>, true)
    caseName case

type private FieldlessUnionConverter<'T>() =
    inherit JsonConverter<'T>()

    let cases = FSharpType.GetUnionCases(typeof<'T>, true)

    let byName =
        cases
        |> Array.map (fun c -> caseName c, FSharpValue.MakeUnion(c, [||], true) :?> 'T)
        |> dict

    let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<'T>, true)

    override _.Read(reader, _, _) =
        let text = reader.GetString()

        match byName.TryGetValue text with
        | true, value -> value
        | _ -> raise (JsonException $"Unknown {typeof<'T>.Name} value '{text}'.")

    override _.Write(writer, value, _) =
        writer.WriteStringValue(caseName cases[tagReader (box value)])

type private FieldlessUnionConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert t =
        FSharpType.IsUnion(t, true)
        && not (t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>)
        && not (t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>)
        && FSharpType.GetUnionCases(t, true) |> Array.forall (fun c -> c.GetFields().Length = 0)

    override _.CreateConverter(t, _) =
        Activator.CreateInstance(typedefof<FieldlessUnionConverter<_>>.MakeGenericType t) :?> JsonConverter

/// F# lists as JSON arrays, in both directions.
type private ListConverter<'T>() =
    inherit JsonConverter<'T list>()

    override _.Read(reader, _, options) =
        JsonSerializer.Deserialize<'T[]>(&reader, options) |> List.ofArray

    override _.Write(writer, value, options) =
        JsonSerializer.Serialize(writer, List.toArray value, options)

type private ListConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert t =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    override _.CreateConverter(t, _) =
        Activator.CreateInstance(typedefof<ListConverter<_>>.MakeGenericType(t.GetGenericArguments()))
        :?> JsonConverter

let options (indented: bool) =
    let o =
        JsonSerializerOptions(
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        )

    o.Converters.Add(FieldlessUnionConverterFactory())
    o.Converters.Add(ListConverterFactory())
    o

let private indentedOptions = options true
let private compactOptions = options false

let serialize (indented: bool) (model: AnalysisModel) =
    JsonSerializer.Serialize(model, (if indented then indentedOptions else compactOptions))

let deserialize (json: string) : AnalysisModel =
    JsonSerializer.Deserialize<AnalysisModel>(json, indentedOptions)
