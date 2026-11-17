/// C# support (plan Phase 7): the routing styles of the Routing fixture written in C#, and the recognizer for
/// C# compiler output.
module Floorplan.Tests.CSharpTests

open Expecto
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.Codegen.CSharp
open Floorplan.Cli

let csharpModel = lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.csharp.Force()))

let private csharpModelNet10 =
    lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.csharpNet10.Force()))

let private entries (model: AnalysisModel) (app: string) =
    model.EntryPoints |> List.filter (fun e -> e.ApplicationId = app)

let private field (typeName: string) (name: string) =
    { Type = { Assembly = "App"; FullName = typeName }
      Name = name
      FieldType = "System.Func`1<System.String>" }

[<Tests>]
let tests =
    testSequenced
    <| testList
        "C#"
        [ test "golden: CSharp fixture" { Golden.check "csharp.entry-points.txt" (EntryPointTests.render (csharpModel.Force())) }

          test "golden: CSharp fixture on net10.0 gives the net8.0 results" {
              let m = csharpModelNet10.Force()
              Expect.isTrue (m.Projects |> List.forall (fun p -> p.TargetFramework = "net10.0")) "every project on net10.0"
              Golden.check "csharp.entry-points.txt" (EntryPointTests.render m)
          }

          test "minimal APIs: one entry per route; cached delegates are not taken for library responses" {
              let m = csharpModel.Force()
              let endpoints = entries m "CSharp.Endpoints"

              Expect.isFalse (endpoints |> List.exists (fun e -> e.HandlerMember.Contains "library function")) "no library responses"

              let keys = endpoints |> List.map (fun e -> e.HttpMethod, e.Route)
              Expect.equal keys (List.distinct keys) "one entry per method and route"

              let byRoute route =
                  endpoints |> List.find (fun e -> e.Route = Some route && e.HttpMethod <> Some "DELETE")

              Expect.stringEnds (byRoute "/api/orders").HandlerMember "Api::ListOrders" "method group"
              Expect.stringEnds (byRoute "/api/orders/{id:int}").HandlerMember "Api::GetOrder" "a lambda that calls a method"
              Expect.stringContains (byRoute "/admin/users").HandlerMember "Admin/<>c" "a lambda is its own handler"
              Expect.equal (byRoute "/admin/reports/rebuild").HttpMethod (Some "POST") "nested group behind a convention chain"
          }

          test "hand-written routing, switch expressions and tables in C# give the F# results" {
              let m = csharpModel.Force()

              let routes app =
                  entries m app
                  |> List.choose (fun e ->
                      match e.Completeness with
                      | Completeness.Complete -> Some(defaultArg e.HttpMethod "-" + " " + defaultArg e.Route "-")
                      | _ -> None)
                  |> set

              for r in [ "GET /health"; "OPTIONS /orders"; "GET /orders/{param}"; "PUT /orders/{param}"; "DELETE /orders/{param}" ] do
                  Expect.contains (routes "CSharp.Middleware") r $"middleware {r}"

              // Seven string cases (Roslyn compares hashes first, then the strings), then list patterns.
              Expect.equal (routes "CSharp.Switch").Count 9 "every case of both switches"
              Expect.contains (routes "CSharp.Switch") "POST /api/orders" "method and path from the tuple"
              Expect.contains (routes "CSharp.Switch") "DELETE /api/orders/{param}" "a list pattern with a parameter"

              let table = entries m "CSharp.Table" |> List.map (fun e -> e.Route) |> List.sort
              Expect.equal table [ Some "/health"; Some "/orders" ] "dictionary initializer keys"
          }

          test "MVC controllers in C#: attribute routes and the default conventional route" {
              let m = csharpModel.Force()

              let routes =
                  entries m "CSharp.Controllers" |> List.map (fun e -> defaultArg e.HttpMethod "*", defaultArg e.Route "-") |> List.sort

              Expect.equal
                  routes
                  [ "*", "/"; "*", "/Home"; "*", "/Home/Index/{id?}"; "GET", "/api/Orders"; "GET", "/api/Orders/{id:int}"; "POST", "/api/Orders" ]
                  "routes"
          }

          test "recognizer: Roslyn's delegate caches start out null" {
              Expect.isTrue (CSharpCodegen.isLazyCache (field "Program/<>c" "<>9__0_1")) "cached lambda"
              Expect.isTrue (CSharpCodegen.isLazyCache (field "App.Routes/<>O" "<0>__List")) "cached method group"
              Expect.isFalse (CSharpCodegen.isLazyCache (field "Program/<>c" "<>9")) "the singleton is set by the static constructor"
              Expect.isFalse (CSharpCodegen.isLazyCache (field "App.Routes" "Table")) "an ordinary static field"
          }

          test "payloads built as C# writes them: interpolated strings and composite format strings" {
              let m = csharpModel.Force()

              let payload memberName category =
                  m.ExitPoints
                  |> List.find (fun x -> x.Member = memberName && x.Category = category)
                  |> fun x -> x.Payload |> Option.bind (fun p -> p.Value)

              Expect.equal (payload "CSharp.Endpoints.Api::GetOrder" EffectCategory.FilesystemRead) (Some "orders/{…}.json") "interpolated"
              Expect.equal (payload "CSharp.Endpoints.Api::CreateOrder" EffectCategory.FilesystemWrite) (Some "orders/{…}.json") "string.Format"
          }

          test "the site shows C# lambdas by their enclosing method, not their compiler-generated names" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floorplan-tests", System.Guid.NewGuid().ToString "N")
                  let index = Floorplan.Rendering.WebsiteRenderer.render (csharpModel.Force()) { Path = dir; SingleFile = false }
                  let page = Browser.dumpDom browser index "#/entry-points"

                  Expect.stringContains page "Admin › lambda in Map" "lambda in a method"
                  Expect.stringContains page "Program › lambda in top-level statements" "lambda in top-level statements"
                  // The compiler's name stays in the tooltip only.
                  Expect.isFalse (page.Contains ">CSharp.Endpoints.Admin/&lt;&gt;c") "no compiler-generated name shown"
                  Expect.isFalse (page.Contains "title=\"\"></a>") "the global namespace links by its label"
                  Expect.stringContains page "(global namespace)</a>" "global namespace label"
          } ]
