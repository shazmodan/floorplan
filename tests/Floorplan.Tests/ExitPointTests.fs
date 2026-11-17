module Floorplan.Tests.ExitPointTests

open System.Text
open Expecto
open Floorplan.Model
open Floorplan.Cli

/// The full model of a fixture, built by the CLI pipeline without rendering.
let private modelOf (solution: Lazy<Floorplan.ProjectLoading.LoadedSolution>) =
    lazy (Pipeline.buildModel ignore Floorplan.Model.Caching.Store.Disabled [] [] (solution.Force()))

let effectsModel = modelOf Fixtures.effects
let effectsModelNet10 = modelOf Fixtures.effectsNet10
let shopModel = modelOf Fixtures.shop
let mixedModel = modelOf Fixtures.mixed

let private exits (model: AnalysisModel) = model.ExitPoints

let private find (model: AnalysisModel) (memberName: string) (category: EffectCategory) =
    exits model |> List.filter (fun x -> x.Member = memberName && x.Category = category)

let private one model memberName category =
    match find model memberName category with
    | [ x ] -> x
    | xs -> failtestf "expected one %A exit point in %s, found %d" category memberName xs.Length

let render (model: AnalysisModel) =
    let b = StringBuilder()

    for x in exits model do
        let payload =
            match x.Payload with
            | Some p -> $"""{Json.unionName p.Kind}:{defaultArg p.Value "-"}"""
            | None -> "-"

        let line =
            match x.Location with
            | Some l -> $"{Golden.relative l.File}:{l.StartLine}"
            | None -> "?"

        b.AppendLine(
            $"""{EffectCategory.name x.Category} | {x.Unit} | {x.Member} | {x.Primitive} | via {defaultArg x.ViaAssembly "-"} | {payload} | {line}"""
        )
        |> ignore

    b.AppendLine("-- diagnostics") |> ignore

    for d in model.Diagnostics |> List.filter (fun d -> d.Severity <> DiagnosticSeverity.Info) do
        b.AppendLine($"{d.Code} | {d.Message}") |> ignore

    b.ToString()

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Exit points"
        [ test "golden: Effects fixture" { Golden.check "effects.exit-points.txt" (render (effectsModel.Force())) }

          test "golden: Effects fixture on net10.0 gives the net8.0 results" {
              let m = effectsModelNet10.Force()
              Expect.isTrue (m.Projects |> List.forall (fun p -> p.TargetFramework = "net10.0")) "every project on net10.0"
              Golden.check "effects.exit-points.txt" (render m)
          }

          test "golden: Shop fixture" { Golden.check "shop.exit-points.txt" (render (shopModel.Force())) }

          test "direct calls of every category, with literal payloads" {
              let m = effectsModel.Force()
              let http = one m "Effects.Direct::fetch" EffectCategory.HttpOut
              Expect.equal http.Payload.Value.Value (Some "https://api.example.org/status") "url"
              Expect.equal http.Unit "Effects.App:Effects.Direct" "attributed to its module"
              Expect.isNone http.ViaAssembly "direct"
              one m "Effects.Direct::readConfig" EffectCategory.FilesystemRead |> ignore
              one m "Effects.Direct::writeReport" EffectCategory.FilesystemWrite |> ignore
              one m "Effects.Direct::runGit" EffectCategory.Process |> ignore
          }

          test "ADO.NET through two providers is found by the DbCommand rule" {
              let m = effectsModel.Force()

              for memberName in [ "Effects.Database::sqlite"; "Effects.Database::postgres" ] do
                  let db = find m memberName EffectCategory.Database
                  Expect.exists db (fun x -> x.RuleId = "database.dbcommand") $"{memberName} executes a command"
          }

          test "the SQL text of a command is its payload: set through CommandText or given to the constructor" {
              let m = effectsModel.Force()

              let sql memberName =
                  find m memberName EffectCategory.Database
                  |> List.filter (fun x -> x.RuleId = "database.dbcommand")
                  |> List.map (fun x -> x.Payload |> Option.bind (fun p -> p.Value))

              Expect.equal (sql "Effects.Database::sqlite") [ Some "DELETE FROM sessions" ] "command.CommandText <- ..."
              Expect.equal (sql "Effects.Database::postgres") [ Some "SELECT count(*) FROM orders" ] "new NpgsqlCommand(sql, connection)"
          }

          test "effects through wrapper libraries name the library and keep the path" {
              let m = effectsModel.Force()
              let dapper = one m "Effects.Wrapped::queryNames" EffectCategory.Database
              Expect.equal dapper.ViaAssembly (Some "Dapper") "via Dapper"
              Expect.isGreaterThan dapper.Evidence.Steps.Length 2 "evidence path"

              let flurl = one m "Effects.Wrapped::fetchUsers" EffectCategory.HttpOut
              Expect.equal flurl.ViaAssembly (Some "Flurl.Http") "via Flurl.Http"
              Expect.stringContains flurl.Primitive "System.Net.Http.HttpClient" "ends at HttpClient"
          }

          test "higher-order code: lambdas, interfaces, async and task are attributed to the module that wrote them" {
              let m = effectsModel.Force()

              let inHigherOrder =
                  exits m |> List.filter (fun x -> x.Unit = "Effects.App:Effects.HigherOrder")

              let categories = inHigherOrder |> List.map (fun x -> x.Category) |> List.countBy id |> Map.ofList
              Expect.equal (categories.TryFind EffectCategory.FilesystemWrite) (Some 2) "lambda and interface implementation"
              Expect.equal (categories.TryFind EffectCategory.HttpOut) (Some 1) "async"
              Expect.equal (categories.TryFind EffectCategory.FilesystemRead) (Some 1) "task"
              Expect.isEmpty (find m "Effects.HigherOrder::process'" EffectCategory.FilesystemWrite) "the higher-order function itself has none"
          }

          test "payloads: templates for interpolation, printf and Path.Combine; dynamic otherwise" {
              let m = effectsModel.Force()
              let payload memberName category = (one m memberName category).Payload.Value

              let user = payload "Effects.Payloads::user" EffectCategory.HttpOut
              Expect.equal (user.Kind, user.Value) (PayloadKind.Template, Some "https://api.example.org/users/{…}") "interpolated"

              let order = payload "Effects.Payloads::order" EffectCategory.HttpOut
              Expect.equal (order.Kind, order.Value) (PayloadKind.Template, Some "https://api.example.org/orders/{…}") "printf"

              let file = payload "Effects.Payloads::file" EffectCategory.FilesystemRead
              Expect.equal (file.Kind, file.Value) (PayloadKind.Template, Some "data/{…}") "Path.Combine"

              let anything = payload "Effects.Payloads::anything" EffectCategory.FilesystemRead
              Expect.equal anything.Kind PayloadKind.Dynamic "dynamic"
          }

          test "native calls, hidden categories and blind spots" {
              let m = effectsModel.Force()
              one m "Effects.Hidden::pid" EffectCategory.Native |> ignore
              one m "Effects.Hidden::now" EffectCategory.Nondeterminism |> ignore
              one m "Effects.Hidden::home" EffectCategory.Environment |> ignore
              Expect.exists m.Diagnostics (fun d -> d.Code = "blind-spot.reflection") "reflection is a blind spot"
          }

          test "Shop: an effect in a C# library is attributed to its namespace" {
              let m = shopModel.Force()

              let storage =
                  exits m |> List.filter (fun x -> x.Unit = "Shop.Storage:Shop.Storage")

              Expect.exists storage (fun x -> x.Category = EffectCategory.FilesystemWrite) "file append in C#"
          }

          test "Mixed: effects in C# code are attributed to the namespace, or the global namespace, they are in" {
              let m = mixedModel.Force()

              Expect.exists
                  (exits m)
                  (fun x -> x.Category = EffectCategory.FilesystemWrite && x.Unit = "Mixed.Billing:Mixed.Billing.Storage")
                  "the file write in a block-scoped namespace"

              Expect.exists
                  (exits m)
                  (fun x ->
                      x.Category = EffectCategory.FilesystemWrite
                      && x.Unit = "Mixed.Billing:Mixed.Billing.Storage"
                      && x.Member.Contains "ArchiveAsync"
                      && x.Payload |> Option.exists (fun p -> p.Value = Some "archive.txt"))
                  "the file write in a lambda inside an async method"

              Expect.exists (exits m) (fun x -> x.Unit = "Mixed.Tool:") "top-level statements"
          }

          test "Shop: the excluded test project contributes nothing" {
              let m = shopModel.Force()
              Expect.isFalse (exits m |> List.exists (fun x -> x.Unit.StartsWith "Shop.Tests")) "no test exits"
          }

          test "Shop: each project records its catalog" {
              let m = shopModel.Force()
              let catalog id = (m.Projects |> List.find (fun p -> p.Id = id)).CatalogUsed
              Expect.equal (catalog "Shop.Web") (Some "net10.0") "net10 app"
              Expect.equal (catalog "Shop.Core") (Some "net8.0") "net8 library"
          } ]
