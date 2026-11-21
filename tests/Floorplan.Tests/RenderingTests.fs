module Floorplan.Tests.RenderingTests

open System
open System.IO
open Expecto
open Floorplan.Model
open Floorplan.Layout
open Floorplan.Rendering

/// A model of the Shop fixture with structure and layout, built without the CLI.
let shopModel =
    lazy
        (let solution = Fixtures.shop.Force()
         let structure = StructureTests.shopStructure.Force()

         let projects =
             solution.Projects
             |> List.map (fun p ->
                 { Id = p.Id
                   Name = p.Name
                   Path = p.Path
                   Language = p.Language
                   SupportLevel = if p.Language = Language.FSharp then SupportLevel.Full else SupportLevel.Limited
                   TargetFramework = p.TargetFramework
                   IsApplication = p.IsApplication
                   IsTestProject = p.IsTestProject
                   CatalogUsed = None
                   CatalogFallback = false
                   ProjectReferences = p.ProjectReferences })

         { AnalysisModel.empty "test" "2026-11-15T00:00:00Z" { Name = "Shop"; Path = solution.Path } with
             Projects = projects
             Units = structure.Units
             Dependencies = structure.Dependencies
             Diagnostics = structure.Diagnostics
             Layout = SolutionLayout.compute projects structure.Units structure.Dependencies })

let private tempDir () =
    let d = Path.Combine(Path.GetTempPath(), "floorplan-test-" + string (Guid.NewGuid()))
    Directory.CreateDirectory d |> ignore
    d

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Rendering"
        [ test "the website writes four files and embeds the model as a script" {
              let dir = tempDir ()

              match (WebsiteRenderer.create ()).Render(shopModel.Force(), { Path = dir; SingleFile = false }) with
              | Error(RenderError e) -> failtest e
              | Ok() ->
                  for f in [ "index.html"; "app.css"; "app.js"; "data.js" ] do
                      Expect.isTrue (File.Exists(Path.Combine(dir, f))) f

                  let data = File.ReadAllText(Path.Combine(dir, "data.js"))
                  Expect.stringStarts data "window.FLOORPLAN_DATA = " "data script"
                  Expect.stringContains data "Shop.Core.Orders" "contains units"
          }

          test "single-file output has no external references" {
              let dir = tempDir ()
              let file = WebsiteRenderer.render (shopModel.Force()) { Path = dir; SingleFile = true }
              let html = File.ReadAllText file
              Expect.isFalse (html.Contains "src=\"app.js\"") "app.js inlined"
              Expect.isFalse (html.Contains "href=\"app.css\"") "app.css inlined"
              Expect.isFalse (html.Contains "http://" && html.Contains "<script src=\"http") "no remote scripts"
              Expect.stringContains html "window.FLOORPLAN_DATA" "data inlined"
          }

          test "data cannot close the script element" {
              let model =
                  { shopModel.Force() with
                      Solution = { Name = "</script><b>x"; Path = "p" } }

              let script = WebsiteRenderer.dataScript model
              Expect.isFalse (script.Contains "</script>") "escaped"
          }

          test "the JSON renderer writes the model, which reads back equal" {
              let dir = tempDir ()
              let file = Path.Combine(dir, "model.json")
              let model = shopModel.Force()

              match (JsonRenderer.create ()).Render(model, { Path = file; SingleFile = false }) with
              | Error(RenderError e) -> failtest e
              | Ok() -> Expect.equal (Json.deserialize (File.ReadAllText file)) model "round trip"
          }

          test "the site renders in a headless browser" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let dir = tempDir ()
                  let index = WebsiteRenderer.render (shopModel.Force()) { Path = dir; SingleFile = false }

                  let overview = Browser.dumpDom browser index ""
                  Expect.stringContains overview "<h1>Shop</h1>" "overview heading"
                  Expect.stringContains overview "limited support" "C# project flagged"

                  let graph = Browser.dumpDom browser index "#/units"
                  Expect.stringContains graph "class=\"g-node" "unit nodes drawn"
                  Expect.stringContains graph "Orders" "unit label"

                  let detail = Browser.dumpDom browser index "#/unit/Shop.Core%3AShop.Core.Orders"
                  Expect.stringContains detail "<h1>Shop.Core.Orders</h1>" "unit detail"
                  Expect.stringContains detail "Validation" "lists a dependency"

                  let projects = Browser.dumpDom browser index "#/projects"
                  Expect.stringContains projects "Shop.Web" "project graph"
          }

          test "the flow view connects routes and side-effects to their units in a headless browser" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let model = ExitPointTests.shopModel.Force()
                  Expect.isNonEmpty model.Layout.FlowGraph.Nodes "the pipeline lays out the flow graph"
                  let dir = tempDir ()
                  let index = WebsiteRenderer.render model { Path = dir; SingleFile = false }
                  let flow = Browser.dumpDom browser index "#/flow"
                  Expect.stringContains flow "class=\"g-card in\"" "entry cards drawn"
                  Expect.stringContains flow "class=\"g-card out cat-" "exit cards drawn"
                  Expect.stringContains flow "flow-unit" "unit nodes drawn"
                  Expect.stringContains flow "#/evidence/entry:" "routes link to their evidence"
                  Expect.stringContains flow "aria-current=\"page\">Flow" "Flow in the menu"
          }

          test "the architecture pages draw the four C4 levels in a headless browser" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let model = ExitPointTests.shopModel.Force()
                  let dir = tempDir ()
                  let index = WebsiteRenderer.render model { Path = dir; SingleFile = false }

                  let context = Browser.dumpDom browser index "#/architecture"
                  Expect.stringContains context "aria-current=\"page\">Architecture" "Architecture in the menu"
                  Expect.stringContains context "c4-el c4-software-system" "the system"
                  Expect.stringContains context "c4-el c4-person" "the user"
                  Expect.stringContains context "prices.example.org" "an external system"

                  let containers = Browser.dumpDom browser index "#/architecture/containers"
                  Expect.stringContains containers "c4-boundary" "the system's boundary"
                  Expect.stringContains containers "Shop.Worker" "a container"
                  Expect.stringContains containers "/api/orders" "the worker calls the web application"

                  let components = Browser.dumpDom browser index "#/architecture/components%3AShop.Web"
                  Expect.stringContains components "Shop.Web.Handlers" "a component"
                  Expect.stringContains components "Shop.Web [Container]" "the container's boundary"

                  let code = Browser.dumpDom browser index "#/architecture/code%3AShop.Web%7CShop.Web%3AShop.Web.Handlers"
                  Expect.stringContains code "FileNotifier" "a type"
                  Expect.stringContains code ">Shop.Web.Handlers</strong>" "the breadcrumb ends at the component"
          } ]
