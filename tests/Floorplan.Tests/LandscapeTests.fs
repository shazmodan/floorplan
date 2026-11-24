/// A folder of solutions analyzed as one landscape (docs/landscape-plan.md), on the Landscape fixture: Orders,
/// Billing and Shipping, which call each other, and a solution nested in Billing's folder.
module Floorplan.Tests.LandscapeTests

open System
open System.IO
open Expecto
open Floorplan.Model
open Floorplan.ProjectLoading
open Floorplan.Architecture
open Floorplan.Cli
open Floorplan.Cli.Options

let private landscapeDir = Fixtures.path "Landscape"

/// The Landscape fixture analyzed once, as `floorplan analyze tests/fixtures/Landscape` would: its output folder and
/// the outcome.
let landscapeRun =
    lazy
        (let out = Path.Combine(Path.GetTempPath(), "floorplan-landscape-" + string (Guid.NewGuid()))

         let options =
             { Options.defaults with
                 Path = Some landscapeDir
                 Output = out
                 Cache = false
                 Quiet = true }

         match Pipeline.folderOfSolutions options with
         | None -> failwith "the Landscape fixture is no folder of solutions"
         | Some found ->
             match Pipeline.runLandscape ignore options found with
             | Ok outcome -> out, outcome
             | Error e -> failwith e)

let private landscape () = (snd (landscapeRun.Force())).Landscape

/// A folder tree of empty solution files, for the search.
let private tree (files: string list) =
    let root = Path.Combine(Path.GetTempPath(), "floorplan-tree-" + string (Guid.NewGuid()))

    for f in files do
        let file = Path.Combine(root, f)
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        File.WriteAllText(file, "<Solution />")

    root

let private relativeTo (root: string) (paths: string list) =
    paths |> List.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'))

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Landscape"
        [ test "finds the solutions of a folder: the first in each branch, skipping build output and hidden folders" {
              let root =
                  tree
                      [ "A/A.sln"
                        "A/samples/Sample.sln"
                        "B/B.sln"
                        "B/B.slnx"
                        "group/C/C.slnx"
                        "group/C/tools/deep/Tool.slnx"
                        "bin/Built.sln"
                        "D/obj/Restored.sln"
                        "D/node_modules/Package.sln"
                        ".hidden/Hidden.sln"
                        "readme.sln.txt" ]

              try
                  let found = Discovery.findSolutions root
                  Expect.equal (relativeTo root found.Solutions) [ "A/A.sln"; "B/B.slnx"; "group/C/C.slnx" ] "a .slnx over its .sln"
                  Expect.equal (relativeTo root found.Nested) [ "A/samples/Sample.sln"; "group/C/tools/deep/Tool.slnx" ] "inside a solution's folder"
                  Expect.isTrue (Discovery.isFolderOfSolutions (Some root)) "no solution of its own"
                  Expect.isFalse (Discovery.isFolderOfSolutions (Some(Path.Combine(root, "A")))) "a solution's folder"
              finally
                  Directory.Delete(root, true)
          }

          test "a folder with a solution or project keeps its meaning; a folder of solutions is new" {
              Expect.isTrue (Discovery.isFolderOfSolutions (Some landscapeDir)) "the Landscape fixture"
              Expect.isTrue (Discovery.isFolderOfSolutions (Some Fixtures.fixturesDir)) "the fixtures folder"
              Expect.isFalse (Discovery.isFolderOfSolutions (Some(Fixtures.path "Shop"))) "a solution's folder"
              Expect.isFalse (Discovery.isFolderOfSolutions (Some(Fixtures.path "Shop/Shop.slnx"))) "a solution file"
              Expect.isNone (Pipeline.folderOfSolutions { Options.defaults with Path = Some(Fixtures.path "Shop") }) "analyzed as a solution"

              let found = Discovery.findSolutions Fixtures.fixturesDir
              let names = found.Solutions |> List.map Path.GetFileNameWithoutExtension

              Expect.equal
                  names
                  [ "CSharp"; "Effects"; "Frameworks"; "Billing"; "Orders"; "Shipping"; "Mixed"; "Routing"; "Shop" ]
                  "each fixture solution, the Landscape fixture's three included"
          }

          test "--solutions names solutions, nested ones too; --exclude-solutions leaves some out" {
              let found = Discovery.findSolutions landscapeDir
              let names (r: Result<string list, string>) = r |> Result.map (List.map Path.GetFileNameWithoutExtension)
              let choose solutions excluded =
                  Pipeline.chooseSolutions { Options.defaults with Solutions = solutions; ExcludeSolutions = excluded } found |> names

              Expect.equal (choose [] []) (Ok [ "Billing"; "Orders"; "Shipping" ]) "every system, not the nested one"
              Expect.equal (choose [ "billing.samples"; "Orders" ] []) (Ok [ "Orders"; "Billing.Samples" ]) "named, any case"
              Expect.equal (choose [] [ "Orders" ]) (Ok [ "Billing"; "Shipping" ]) "excluded"

              match choose [ "Nowhere" ] [] with
              | Error e -> Expect.stringContains e "No solution named nowhere" "names the unknown one"
              | Ok _ -> failtest "an unknown solution was accepted"
          }

          test "slugs: lower case, safe as a folder name, unique" {
              Expect.equal (Landscape.slugsOf [ "Shop"; "My App"; "shop"; "Shop" ]) [ "shop"; "my-app"; "shop-2"; "shop-3" ] "slugs"
          }

          test "writes each solution's site under solutions/<slug>/ and the landscape beside them" {
              let out, outcome = landscapeRun.Force()
              let l = outcome.Landscape

              Expect.isFalse outcome.HadFailures "every solution analyzed"
              Expect.equal outcome.Written (Path.GetFullPath(Path.Combine(out, "index.html"))) "the landscape's site"
              Expect.equal (l.Solutions |> List.map (fun s -> s.Name, s.Slug, s.Path.Replace('\\', '/')))
                  [ "Billing", "billing", "Billing/Billing.slnx"
                    "Orders", "orders", "Orders/Orders.slnx"
                    "Shipping", "shipping", "Shipping/Shipping.slnx" ]
                  "the solutions"
              Expect.equal (l.Nested |> List.map (fun n -> n.Replace('\\', '/'))) [ "Billing/samples/Billing.Samples.slnx" ] "the nested one"
              Expect.equal l.Name "Landscape" "named after the folder"

              let orders = l.Solutions |> List.find (fun s -> s.Name = "Orders")
              Expect.equal (orders.Applications, orders.Projects, orders.Languages) (1, 1, [ Language.CSharp ]) "a summary of each"
              Expect.equal orders.EntryPoints 3 "its routes"

              for s in l.Solutions do
                  let site = Path.Combine(out, "solutions", s.Slug)
                  Expect.isTrue (File.Exists(Path.Combine(site, "index.html"))) $"{s.Name}'s site"
                  let data = File.ReadAllText(Path.Combine(site, "data.js"))
                  Expect.stringContains data "window.FLOORPLAN_LANDSCAPE_LINK = {" $"{s.Name}'s way back"
                  Expect.stringContains data "\"landscape\":\"Landscape\"" "the landscape's name"
                  Expect.stringContains data $"\"current\":\"{s.Slug}\"" "which one it is"

              let data = File.ReadAllText(Path.Combine(out, "data.js"))
              Expect.stringStarts data "window.FLOORPLAN_LANDSCAPE = " "the landscape's data"
              let json = data.Substring("window.FLOORPLAN_LANDSCAPE = ".Length, data.IndexOf(";\nwindow.FLOORPLAN_FILES") - "window.FLOORPLAN_LANDSCAPE = ".Length)
              let back = Json.deserializeValue<Landscape> json
              Expect.equal back.Solutions l.Solutions "reads back"
          }

          test "--format json writes landscape.json and each solution's floorplan.json" {
              let out = Path.Combine(Path.GetTempPath(), "floorplan-landscape-json-" + string (Guid.NewGuid()))
              let options = { Options.defaults with Path = Some landscapeDir; Output = out; Format = Json; Build = false; Cache = false; Quiet = true }

              try
                  match Pipeline.runLandscape ignore options (Pipeline.folderOfSolutions options).Value with
                  | Error e -> failtest e
                  | Ok outcome ->
                      Expect.equal outcome.Written (Path.GetFullPath(Path.Combine(out, "landscape.json"))) "the landscape"
                      let back = Json.deserializeValue<Landscape> (File.ReadAllText outcome.Written)
                      Expect.equal (back.Solutions |> List.map (fun s -> s.Slug)) [ "billing"; "orders"; "shipping" ] "its solutions"

                      for slug in [ "billing"; "orders"; "shipping" ] do
                          let model = Json.deserialize (File.ReadAllText(Path.Combine(out, "solutions", slug, "floorplan.json")))
                          Expect.isNonEmpty model.Projects $"{slug}'s model"
              finally
                  try Directory.Delete(out, true) with _ -> ()
          }

          test "a solution that cannot be analyzed is listed with why; the others are analyzed" {
              let root = tree [ "Empty/Empty.slnx" ]
              File.Copy(Path.Combine(ArchitectureTests.repoRoot, "global.json"), Path.Combine(root, "global.json"))
              let out = Path.Combine(root, "out")
              let options = { Options.defaults with Path = Some root; Output = out; Build = false; Cache = false; Quiet = true }

              try
                  match Pipeline.runLandscape ignore options (Pipeline.folderOfSolutions options).Value with
                  | Error e -> failtest e
                  | Ok outcome ->
                      Expect.isTrue outcome.HadFailures "the run failed"
                      let s = outcome.Landscape.Solutions.Head
                      Expect.isSome s.Failure "why"
                      Expect.isTrue (outcome.Landscape.Diagnostics |> List.exists (fun d -> d.Code = "solution-failed" && d.Message.StartsWith "[Empty]")) "a diagnostic"
                      Expect.isTrue (File.Exists outcome.Written) "the landscape is still written"
              finally
                  try Directory.Delete(root, true) with _ -> ()
          }

          test "browser: the landscape lists its solutions, and a solution's site links back" {
              match Browser.find () with
              | None -> skiptest "no Chromium-based browser found (set FLOORPLAN_BROWSER)"
              | Some browser ->
                  let out, _ = landscapeRun.Force()
                  let index = Browser.dumpDom browser (Path.Combine(out, "index.html")) ""
                  Expect.stringContains index "<title>Landscape · Floorplan</title>" "titled after the folder"
                  Expect.stringContains index "href=\"solutions/orders/index.html#/overview\">Orders</a>" "links to each solution's site"
                  Expect.stringContains index "Billing/samples/Billing.Samples.slnx" "lists the nested solution"

                  let orders = Browser.dumpDom browser (Path.Combine(out, "solutions", "orders", "index.html")) "#/overview"
                  Expect.stringContains orders "<title>Orders · Landscape · Floorplan</title>" "titled in its landscape"
                  Expect.stringContains orders "href=\"../../index.html#/solutions\"" "the way back"
                  Expect.stringContains orders "<option value=\"shipping\">Shipping</option>" "the other solutions"
          }

          test "golden: the Landscape fixture" {
              Golden.check "landscape.txt" ((Landscape.describe (landscape ()) |> String.concat "\n") + "\n")
          }

          test "matching: a queue, a host that names the system, the landscape file, a route only one system has" {
              let l = landscape ()

              let found =
                  l.Interactions |> List.map (fun i -> i.FromContainer, i.ToContainer, i.What, i.Methods, i.MatchedBy, i.Probable)

              Expect.equal
                  found
                  [ "container:billing/Billing.Api", "container:orders/Orders.Api", "/orders/{id}", [ "GET" ], "route-only", true
                    "container:orders/Orders.Api", "container:billing/Billing.Api", "/invoices/{id}", [ "GET" ], "host", false
                    "container:orders/Orders.Api", "container:shipping/Shipping.Worker", "shipments", [], "queue", false
                    "container:shipping/Shipping.Worker", "container:orders/Orders.Api", "/orders/{id}/delivered", [ "PUT" ], "landscape-file", false ]
                  "every call between the systems, and how it was matched"

              let ship = l.Interactions |> List.find (fun i -> i.MatchedBy = "queue")
              Expect.equal ship.Technology (Some "RabbitMQ.Client") "how the systems talk"
              Expect.equal ship.Sources.Length 2 "the exit point and the entry point"
              Expect.isTrue (ship.Sources |> List.forall (fun s -> l.Sources |> List.exists (fun x -> x.Id = s))) "each source is described"
          }

          test "a matched call goes to the other system, not to the external system its host names" {
              let l = landscape ()
              let ids = l.Architecture.Elements |> List.map (fun e -> e.Id)

              Expect.containsAll ids [ "system:billing"; "system:orders"; "system:shipping" ] "a system per solution"
              Expect.contains ids "container:orders/Orders.Api" "its applications as containers"
              Expect.isFalse (ids |> List.contains "external:http:billing-api.internal") "the host of a matched call"
              Expect.contains ids "external:http:inventory.example.net" "a call that matches nothing reaches an external system"
              Expect.contains ids "external:filesystem@billing" "the file system is each system's own"
              Expect.contains ids "person:user" "one user for every system"

              let parent = l.Architecture.Elements |> List.find (fun e -> e.Id = "container:orders/Orders.Api") |> fun e -> e.Parent
              Expect.equal parent (Some "system:orders") "in its system"
          }

          test "without the landscape file, a host that names nothing leaves only a route, matched as probable" {
              let _, outcome = landscapeRun.Force()
              let l = Landscape.derive "test" "now" landscapeDir Landscape.noFile [] outcome.Analyzed
              let delivered = l.Interactions |> List.find (fun i -> i.What = "/orders/{id}/delivered")
              Expect.equal (delivered.MatchedBy, delivered.Probable) ("route-only", true) "probable"
          }

          test "a landscape file that names an unknown system says so" {
              let _, outcome = landscapeRun.Force()
              let file = { Landscape.Hosts = Map.ofList [ "api.example.com", "Nowhere" ] }
              let l = Landscape.derive "test" "now" landscapeDir file [] outcome.Analyzed
              Expect.isTrue (l.Diagnostics |> List.exists (fun d -> d.Code = "landscape-file.unknown-system")) "a warning"
              Expect.isFalse (l.Interactions |> List.exists (fun i -> i.What = "/orders/{id}/delivered")) "the file decides: nowhere"
          }

          test "routes: parameters take any segment, a run-time segment only a parameter, a catch-all nothing" {
              Expect.isTrue (Landscape.routeMatches "/orders/17" "/orders/{id}") "a parameter"
              Expect.isTrue (Landscape.routeMatches "/orders/{…}" "/orders/{id}") "a run-time segment and a parameter"
              Expect.isFalse (Landscape.routeMatches "/orders/{…}" "/orders/open") "a run-time segment and a literal"
              Expect.isTrue (Landscape.routeMatches "/Orders" "/orders") "any case"
              Expect.isTrue (Landscape.routeMatches "/orders" "/orders/{id?}") "an optional parameter"
              Expect.isTrue (Landscape.routeMatches "/files/a/b" "/files/{*path}") "a catch-all after a literal"
              Expect.isFalse (Landscape.routeMatches "/x/y" "/{service}/{method}") "parameters only"
              Expect.isFalse (Landscape.routeMatches "/orders/17/lines" "/orders/{id}") "more segments"
              Expect.equal (Landscape.hostOf "https://Billing-Api.internal:8443/x") (Some "billing-api.internal") "host"
              Expect.equal (Landscape.hostOf "https://{…}/x") None "a host only known at run time"
              Expect.equal (Landscape.pathOf "https://h/a/b?c=1#d") "/a/b" "path"
          }

          test "solutions that do not call each other give no link but by route only, and those are probable" {
              let analyzed =
                  [ "CSharp", CSharpTests.csharpModel
                    "Routing", EntryPointTests.routingModel
                    "Shop", ExitPointTests.shopModel
                    "Frameworks", FrameworkTests.frameworksModel ]
                  |> List.map (fun (name, m) -> ({ File = Fixtures.path $"{name}/{name}.slnx"; Model = Ok(m.Force()) }: Landscape.Analyzed))

              let l = Landscape.derive "test" "now" Fixtures.fixturesDir Landscape.noFile [] analyzed
              Expect.isTrue (l.Interactions |> List.forall (fun i -> i.Probable)) $"only probable links: %A{l.Interactions}"

              Expect.equal
                  (l.Interactions |> List.map (fun i -> i.FromContainer, i.ToContainer, i.What))
                  [ "container:csharp/CSharp.Clients", "container:routing/Routing.Conventional", "/Products" ]
                  "CSharp.Clients calls /products, which only Routing.Conventional has"
          }

          test "views: the systems, each system's containers, and every container in its system" {
              let l = landscape ()
              let view (id: string) = l.Architecture.Views |> List.find (fun v -> v.Id = id)

              Expect.equal
                  (l.Architecture.Views |> List.map (fun v -> v.Id))
                  [ "context"; "context-all"; "containers"; "containers:billing"; "containers:orders"; "containers:shipping" ]
                  "the views"

              let context = view "context"
              Expect.equal context.Scope Landscape.LandscapeScope "of the whole landscape"
              Expect.equal context.Elements [ "system:billing"; "system:orders"; "system:shipping"; "person:user" ] "the systems and what more than one uses"
              Expect.contains (context.Relationships |> List.map (fun r -> r.From, r.To)) ("system:orders", "system:shipping") "lifted to the systems"
              Expect.contains (view "context-all").Elements "external:http:inventory.example.net" "with every external system"

              let orders = view "containers:orders"
              Expect.equal orders.Scope "system:orders" "of one system"
              Expect.containsAll orders.Elements [ "container:orders/Orders.Api"; "container:billing/Billing.Api"; "container:shipping/Shipping.Worker" ] "its containers and those it talks to"

              for v in l.Architecture.Views do
                  let laid = v.Layout.Nodes |> List.map (fun n -> n.Id) |> Set.ofList
                  Expect.equal laid (Set.ofList v.Elements) $"{v.Id}: every element laid out"
          }

          test "the view of every container draws each system's containers apart from the rest" {
              let l = landscape ()
              let v = l.Architecture.Views |> List.find (fun v -> v.Id = "containers")
              let parent (id: string) = l.Architecture.Elements |> List.find (fun e -> e.Id = id) |> fun e -> e.Parent
              let boxes = v.Layout.Nodes

              let overlaps (a: NodeLayout) (b: NodeLayout) =
                  a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height

              for a in boxes do
                  for b in boxes do
                      if a.Id < b.Id then
                          Expect.isFalse (overlaps a b) $"{a.Id} and {b.Id} apart"

              // A system's boundary (its containers' box, as the website pads it) holds nothing of another system.
              for system in [ "system:billing"; "system:orders"; "system:shipping" ] do
                  let mine = boxes |> List.filter (fun n -> parent n.Id = Some system)
                  let x0, y0 = (mine |> List.map (fun n -> n.X) |> List.min) - 28.0, (mine |> List.map (fun n -> n.Y) |> List.min) - 58.0
                  let x1, y1 = (mine |> List.map (fun n -> n.X + n.Width) |> List.max) + 28.0, (mine |> List.map (fun n -> n.Y + n.Height) |> List.max) + 28.0

                  for n in boxes |> List.filter (fun n -> parent n.Id <> Some system) do
                      Expect.isFalse (n.X < x1 && x0 < n.X + n.Width && n.Y < y1 && y0 < n.Y + n.Height) $"{n.Id} outside {system}'s boundary"
          }

          test "golden: the landscape as a Structurizr workspace, probable relationships dashed" {
              let out, _ = landscapeRun.Force()
              let dsl = File.ReadAllText(Path.Combine(out, "workspace.dsl"))
              Golden.check "landscape.workspace.dsl" dsl
              Expect.stringContains dsl "systemLandscape \"context\"" "a landscape view"
              Expect.stringContains dsl "\"sends HTTP requests to /orders/{id}\" \"HTTP\" \"Probable\"" "the probable one tagged"
              Expect.stringContains dsl "dashed true" "and drawn dashed"
              Expect.stringContains (File.ReadAllText(Path.Combine(out, "workspace.json"))) "\"systemLandscapeViews\"" "its layout"
          }

          test "browser: the landscape's views, a probable line dashed, a boundary per system" {
              match Browser.find () with
              | None -> skiptest "no Chromium-based browser found (set FLOORPLAN_BROWSER)"
              | Some browser ->
                  let out, _ = landscapeRun.Force()
                  let index = Path.Combine(out, "index.html")
                  let context = Browser.dumpDom browser index "#/architecture/context"
                  Expect.stringContains context ">Landscape</a>" "the Landscape page in the menu"
                  Expect.stringContains context "c4-rel c4-probable" "the route-only link dashed"
                  Expect.stringContains context "probable: route only" "and labelled"

                  let every = Browser.dumpDom browser index "#/architecture/containers"
                  let count (text: string) (s: string) = s.Split(text).Length - 1
                  Expect.equal (count "class=\"c4-boundary\"" every) 3 "a boundary per system"
                  Expect.isFalse (every.Contains "c4-container c4-outside") "every container is the view's own"

                  let orders = Browser.dumpDom browser index "#/architecture/containers:orders"
                  Expect.stringContains orders "open site ↗" "a container opens its solution's site"
          }

          test "a solution's own C4 model shows the systems of its landscape it talks to, not their hosts" {
              let l = landscape ()
              let known = Landscape.knownOf l "orders"
              let toBilling = known.Sends |> Map.toList |> List.map (fun (_, (o, d)) -> o.Name, d)
              Expect.contains toBilling ("Billing", "sends HTTP requests to /invoices/{id}") "what Orders sends Billing"
              Expect.equal (known.Receives |> Map.toList |> List.collect snd |> List.map (fun o -> o.Name) |> List.sort) [ "Billing"; "Shipping" ] "who calls Orders"

              let out, outcome = landscapeRun.Force()
              let original = outcome.Analyzed |> List.pick (fun a -> match a.Model with Ok m when m.Solution.Name = "Orders" -> Some m | _ -> None)
              let c4 = C4.deriveIn known original
              let billing = c4.Elements |> List.find (fun e -> e.Id = "external:system:billing")
              Expect.equal (billing.Name, billing.Kind, billing.Tags) ("Billing", C4Kind.ExternalSystem, [ "in the landscape" ]) "Billing, as a system of the landscape"
              Expect.isFalse (c4.Elements |> List.exists (fun e -> e.Id = "external:http:billing-api.internal")) "not the host it is called at"

              let context = c4.Views |> List.find (fun v -> v.Id = "context")
              Expect.contains (context.Relationships |> List.map (fun r -> r.From, r.To, r.Description)) (C4.SystemId, "external:system:billing", "sends HTTP requests to /invoices/{id}") "what it sends there"
              Expect.contains (context.Relationships |> List.map (fun r -> r.From, r.To)) ("external:system:shipping", C4.SystemId) "Shipping calls it"
              Expect.isFalse ((C4.derive original).Elements |> List.exists (fun e -> e.Id.StartsWith "external:system:")) "alone, no system of a landscape"

              let data = File.ReadAllText(Path.Combine(out, "solutions", "orders", "data.js"))
              Expect.stringContains data "\"id\":\"external:system:billing\"" "its site has it"
          }

          test "browser: the Interactions page, and another system in a solution's site opens that system's site" {
              match Browser.find () with
              | None -> skiptest "no Chromium-based browser found (set FLOORPLAN_BROWSER)"
              | Some browser ->
                  let out, _ = landscapeRun.Force()
                  let page = Browser.dumpDom browser (Path.Combine(out, "index.html")) "#/interactions"
                  let count (text: string) (s: string) = s.Split(text).Length - 1
                  Expect.equal (count "exit point ↗" page) 4 "every interaction"
                  Expect.stringContains page "route only (probable)" "the probable one"
                  Expect.stringContains page "href=\"solutions/billing/index.html#/evidence/exit:" "its evidence in the caller's site"
                  Expect.stringContains page "landscape file" "matched by the landscape file"

                  let from = Browser.dumpDom browser (Path.Combine(out, "index.html")) "#/interactions?from=system%3Aorders"
                  Expect.equal (count "exit point ↗" from) 2 "filtered by caller"

                  let orders = Browser.dumpDom browser (Path.Combine(out, "solutions", "orders", "index.html")) "#/architecture/context"
                  Expect.stringContains orders "Double-click to open it in Billing's site." "Billing opens its site"
          } ]
