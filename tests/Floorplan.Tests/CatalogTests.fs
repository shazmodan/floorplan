module Floorplan.Tests.CatalogTests

open System.IO
open Expecto
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

let private methodRef (typeName: string) (name: string) (parameters: string list) =
    let joined = String.concat "," parameters
    let localKey = $"{typeName}::{name}`0({joined})"

    { Type = { Assembly = "System.Runtime"; FullName = typeName }
      Name = name
      Key = "[System.Runtime]" + localKey
      LocalKey = localKey
      ParameterTypes = parameters
      ReturnType = "System.Void"
      HasThis = true
      GenericArity = 0
      TypeArguments = [] }

/// A hierarchy for tests: a provider command derives from DbCommand.
let private supertypes (t: TypeRef) =
    match t.FullName with
    | "Provider.SqlCommand" -> set [ "Provider.SqlCommand"; "System.Data.Common.DbCommand"; "System.Data.IDbCommand"; "System.Object" ]
    | other -> set [ other; "System.Object" ]

[<Tests>]
let tests =
    testList
        "Catalog"
        [ test "the built-in catalogs load" {
              Expect.containsAll CatalogLoader.builtInNames [ "net8.0"; "net10.0" ] "built-in catalogs"
              let net8 = CatalogLoader.loadBuiltIn "net8.0"
              Expect.isGreaterThan net8.Rules.Length 50 "a full catalog"
          }

          test "net10.0 is a delta on net8.0" {
              let net8 = CatalogLoader.loadBuiltIn "net8.0"
              let net10 = CatalogLoader.loadBuiltIn "net10.0"
              Expect.exists net10.Rules (fun r -> r.Id = "http-out.httpclient") "inherits net8.0 rules"
              Expect.exists net10.Rules (fun r -> r.Id = "nondeterminism.guid-v7") "adds its own"
              Expect.isFalse (net8.Rules |> List.exists (fun r -> r.Id = "nondeterminism.guid-v7")) "not in net8.0"

              let append = net10.Rules |> List.filter (fun r -> r.Id = "filesystem-write.file")
              Expect.equal append.Length 1 "replaced, not duplicated"
              Expect.contains append.Head.Members "AppendAllBytes" "extended member list"
          }

          test "chooses the newest catalog not newer than the target, and flags unknown versions" {
              let pick tfm =
                  let c = CatalogLoader.forTargetFramework tfm
                  c.Catalog.Name, c.Fallback

              Expect.equal (pick "net8.0") ("net8.0", false) "net8"
              Expect.equal (pick "net9.0") ("net8.0", false) "net9 uses net8"
              Expect.equal (pick "net10.0") ("net10.0", false) "net10"
              Expect.equal (pick "net10.0-windows") ("net10.0", false) "platform suffix"
              Expect.equal (pick "net14.0") ("net10.0", true) "unknown version falls back"
              Expect.equal (pick "netstandard2.0") ("net8.0", false) "netstandard"
          }

          test "matches sinks by type and member" {
              let m = Matcher(CatalogLoader.loadBuiltIn "net8.0", supertypes)

              let sink = m.Sink(methodRef "System.Net.Http.HttpClient" "GetStringAsync" [ "System.String" ])
              Expect.equal (sink |> Option.map (fun r -> r.Id)) (Some "http-out.httpclient") "http client"

              let read = m.Sink(methodRef "System.IO.File" "ReadAllText" [ "System.String" ])
              Expect.equal (read |> Option.bind (fun r -> r.Category)) (Some EffectCategory.FilesystemRead) "file read"

              Expect.isNone (m.Sink(methodRef "System.String" "Concat" [])) "pure"
          }

          test "matches overrides in derived types" {
              let m = Matcher(CatalogLoader.loadBuiltIn "net8.0", supertypes)
              let sink = m.Sink(methodRef "Provider.SqlCommand" "ExecuteReader" [])
              Expect.equal (sink |> Option.bind (fun r -> r.Category)) (Some EffectCategory.Database) "any ADO.NET provider"
          }

          test "constructor rules can be limited to an overload" {
              let m = Matcher(CatalogLoader.loadBuiltIn "net8.0", supertypes)
              Expect.isSome (m.Sink(methodRef "System.IO.StreamReader" ".ctor" [ "System.String" ])) "path overload"
              Expect.isNone (m.Sink(methodRef "System.IO.StreamReader" ".ctor" [ "System.IO.Stream" ])) "stream overload"
          }

          test "finds sources, registrations and blind spots" {
              let m = Matcher(CatalogLoader.loadBuiltIn "net8.0", supertypes)
              let path = m.Source(methodRef "Microsoft.AspNetCore.Http.HttpRequest" "get_Path" [])
              Expect.equal (path |> Option.bind (fun r -> r.RequestPart)) (Some RequestPart.Path) "request path"

              let get =
                  m.Registration(methodRef "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions" "MapGet" [])

              Expect.equal (get |> Option.bind (fun r -> r.HttpMethod)) (Some "GET") "MapGet"
              Expect.isSome (m.BlindSpot(methodRef "System.Activator" "CreateInstance" [])) "reflection"
          }

          test "user catalogs add and replace rules" {
              let file = Path.Combine(Path.GetTempPath(), "floorplan-extra-catalog.json")

              File.WriteAllText(
                  file,
                  """{ "name": "inhouse", "add": [ { "id": "network.inhouse-bus", "type": "Acme.Bus", "members": ["Publish"], "kind": "sink", "category": "network" } ] }"""
              )

              let extended = CatalogLoader.extend (CatalogLoader.loadBuiltIn "net8.0") [ CatalogLoader.loadFile file ]
              let m = Matcher(extended, supertypes)
              Expect.isSome (m.Sink(methodRef "Acme.Bus" "Publish" [])) "in-house boundary"
              Expect.equal extended.Name "net8.0+inhouse" "name"
          }

          test "draft catalogs resolve against the built-in catalogs they extend" {
              let drafts = Path.Combine(ArchitectureTests.repoRoot, "src", "Floorplan.Catalog", "drafts")

              for file in Directory.GetFiles(drafts, "*.json") do
                  let catalog = CatalogLoader.resolveFile file
                  Expect.exists catalog.Rules (fun r -> r.Id = "http-out.httpclient") $"{file} inherits the base rules"

              let net11 = CatalogLoader.resolveFile (Path.Combine(drafts, "net11.0.json"))
              let m = Matcher(net11, supertypes)
              Expect.isSome (m.Sink(methodRef "System.Diagnostics.Process" "RunAsync" [ "System.String" ])) "Process.RunAsync"
              Expect.isSome (m.Sink(methodRef "System.Net.Dns" "ResolveMx" [ "System.String" ])) "Dns.ResolveMx"
          }

          test "zip archives on disk are file effects" {
              let m = Matcher(CatalogLoader.loadBuiltIn "net8.0", supertypes)
              let sink = m.Sink(methodRef "System.IO.Compression.ZipFile" "ExtractToDirectory" [ "System.String"; "System.String" ])
              Expect.equal (sink |> Option.bind (fun r -> r.Category)) (Some EffectCategory.FilesystemWrite) "extract"
          }

          test "rejects malformed catalogs with a clear message" {
              let bad = """{ "name": "bad", "rules": [ { "id": "x", "type": "T", "members": ["M"], "kind": "sink" } ] }"""

              Expect.throwsC
                  (fun () -> CatalogLoader.parse "bad" bad |> ignore)
                  (fun e -> Expect.stringContains e.Message "need a category" "message")
          } ]
