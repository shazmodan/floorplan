/// Payloads of effects behind a library, worked out from the library's code (docs/library-payloads-plan.md).
/// The fixture is CSharp.Effects calling Dependency/CSharp.Wrappers. A pending test is enabled by the iteration
/// that makes it pass; the plan names which.
module Floorplan.Tests.LibraryPayloadTests

open System
open System.IO
open Expecto
open Floorplan.Model
open Floorplan.Cli

let private model () = CSharpTests.csharpModel.Force()

let private exitOf (memberName: string) (category: EffectCategory) =
    let m = model ()

    match m.ExitPoints |> List.filter (fun x -> x.Member = "CSharp.Effects.ThroughWrappers::" + memberName && x.Category = category) with
    | [ x ] -> x
    | xs -> failtestf "expected one %A exit point in %s, found %d" category memberName xs.Length

let private sql memberName =
    (exitOf memberName EffectCategory.Database).Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value)

let private literal role (text: string) = Some(role, PayloadKind.Literal, Some text)
let private template role (text: string) = Some(role, PayloadKind.Template, Some text)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Library payloads"
        [ test "the wrapper library is a dependency: its effects are reported at the call sites, via the library" {
              for memberName in
                  [ "ExecLiteral"; "RunChain"; "CountLiteral"; "CountDynamic"; "ScalarReturned"; "ExecuteQueryObject"
                    "ExecuteDefinition"; "ExecAsyncLiteral"; "RowsLiteral"; "ThroughDictionary"; "Interpolated" ] do
                  Expect.equal (exitOf memberName EffectCategory.Database).ViaAssembly (Some "CSharp.Wrappers") memberName

              Expect.equal (exitOf "FetchLiteral" EffectCategory.HttpOut).ViaAssembly (Some "CSharp.Wrappers") "FetchLiteral"
          }

          // Iteration 2: arguments passed straight through.
          test "a: the argument is the command text" { Expect.equal (sql "ExecLiteral") (literal "sql" "DELETE FROM sessions") "" }

          test "b: the argument is the URL" {
              let url = (exitOf "FetchLiteral" EffectCategory.HttpOut).Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value)
              Expect.equal url (literal "url" "https://api.example.org/status") ""
          }

          test "c: passed on through another library call" { Expect.equal (sql "RunChain") (literal "sql" "DELETE FROM carts") "" }

          test "i: through a dictionary the SQL is not followed" {
              Expect.equal (sql "ThroughDictionary") (Some("sql", PayloadKind.Dynamic, None)) ""
          }

          test "a warm summary cache gives the same payloads" {
              let dir = Path.Combine(Path.GetTempPath(), "floorplan-cache-" + string (Guid.NewGuid()))
              let store = Caching.Store(Some dir)
              let solution = Fixtures.csharp.Force()

              let payloads (m: AnalysisModel) =
                  m.ExitPoints |> List.map (fun x -> x.Member, x.Category, x.Primitive, x.Payload)

              let cold = Pipeline.buildModel ignore store [] [] solution
              let cachedOrigins =
                  Directory.GetFiles(Path.Combine(dir, "summaries"), "*", SearchOption.AllDirectories)
                  |> Array.exists (fun f -> File.ReadAllText(f).Contains "\"parameter\":1")

              Expect.isTrue cachedOrigins "origins in terms of arguments are cached"
              let warm = Pipeline.buildModel ignore store [] [] solution
              Expect.equal (payloads warm) (payloads cold) "same payloads"
              Expect.equal (payloads cold) (payloads (model ())) "the same as without a cache"
              try Directory.Delete(dir, true) with _ -> ()
          }

          // Iteration 3: values built inside the library (j already passes in iteration 2: the argument is passed through).
          test "d: built inside the library from a literal argument" {
              Expect.equal (sql "CountLiteral") (literal "sql" "SELECT count(*) FROM orders") ""
          }

          test "d: built inside the library from an unknown argument" {
              Expect.equal (sql "CountDynamic") (template "sql" "SELECT count(*) FROM {…}") ""
          }

          test "j: an interpolated argument is a template" {
              Expect.equal (sql "Interpolated") (template "sql" "DELETE FROM orders WHERE id = {…}") ""
          }

          // Iteration 4: properties, fields, constructors, returned objects.
          test "e: the command is made and returned by another library method" {
              Expect.equal (sql "ScalarReturned") (literal "sql" "SELECT max(id) FROM orders") ""
          }

          test "f: kept in an object the caller made" {
              Expect.equal (sql "ExecuteQueryObject") (literal "sql" "UPDATE orders SET seen = 1") ""
          }

          test "k: kept in a struct the library made, passed by reference" {
              Expect.equal (sql "ExecuteDefinition") (literal "sql" "DELETE FROM logs") ""
          }

          // Iteration 5: state machines.
          test "g: an async library method" { Expect.equal (sql "ExecAsyncLiteral") (literal "sql" "DELETE FROM tokens") "" }

          test "h: an iterator library method" { Expect.equal (sql "RowsLiteral") (literal "sql" "SELECT name FROM users") "" }

          // F#: the same through an F# wrapper (Effects fixture, Dependency/Effects.Wrappers).
          test "F#: the argument, a record field, a task and a sequence expression" {
              let m = ExitPointTests.effectsModel.Force()

              let fsql memberName =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = "Effects.ThroughWrappers::" + memberName && x.Category = EffectCategory.Database)
                  |> List.map (fun x -> x.ViaAssembly, x.Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value))

              let via = Some "Effects.Wrappers"
              Expect.equal (fsql "execLiteral") [ via, literal "sql" "DELETE FROM sessions" ] "argument"
              Expect.equal (fsql "executeRecord") [ via, literal "sql" "UPDATE orders SET seen = 1" ] "record field"
              Expect.equal (fsql "execTaskLiteral") [ via, literal "sql" "DELETE FROM tokens" ] "task { }"
              Expect.equal (fsql "rowsLiteral") [ via, literal "sql" "SELECT name FROM users" ] "seq { }"
          }

          // Iteration 6: Dapper, in both fixtures.
          test "Dapper: the SQL given to Query and Execute, in C# and F#" {
              let dapper (m: AnalysisModel) memberName =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = memberName && x.Category = EffectCategory.Database)
                  |> List.map (fun x -> x.ViaAssembly, x.Primitive, x.Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value))

              let via = Some "Dapper"
              let query = "System.Data.IDbCommand::ExecuteReader"
              let execute = "System.Data.IDbCommand::ExecuteNonQuery"
              let cs = model ()
              let fs = ExitPointTests.effectsModel.Force()

              Expect.equal (dapper cs "CSharp.Effects.Wrapped::QueryNames") [ via, query, literal "sql" "SELECT name FROM users" ] "C# Query"
              Expect.equal (dapper cs "CSharp.Effects.Wrapped::DeleteSessions") [ via, execute, literal "sql" "DELETE FROM sessions" ] "C# Execute"
              Expect.equal (dapper fs "Effects.Wrapped::queryNames") [ via, query, literal "sql" "SELECT name FROM users" ] "F# Query"
              Expect.equal (dapper fs "Effects.Wrapped::deleteSessions") [ via, execute, literal "sql" "DELETE FROM sessions" ] "F# Execute"
          }

          // After the review on a real solution: Dapper's async methods wrap the SQL in a CommandDefinition and run
          // it in their own state machines.
          test "Dapper: the SQL given to its async methods" {
              let sqlIn (m: AnalysisModel) (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member.StartsWith memberName && x.Category = EffectCategory.Database)
                  |> List.map (fun x -> x.ViaAssembly, x.Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value))
                  |> List.distinct

              let sql = sqlIn (model ())
              let via = Some "Dapper"
              let wrapped = "CSharp.Effects.Wrapped/<"
              Expect.equal (sql (wrapped + "QueryNameAsync>")) [ via, literal "sql" "SELECT name FROM users WHERE id = 1" ] "QuerySingleAsync"
              Expect.equal (sql (wrapped + "QueryNamesAsync>")) [ via, literal "sql" "SELECT name FROM users" ] "QueryAsync"
              Expect.equal (sql (wrapped + "CountSessionsAsync>")) [ via, literal "sql" "SELECT COUNT(*) FROM sessions" ] "ExecuteScalarAsync"
              Expect.equal (sql "CSharp.Effects.Wrapped::DeleteSessionsAsync") [ via, literal "sql" "DELETE FROM sessions" ] "ExecuteAsync"

              // F#: the effect is reported where the task's code runs, the method or its state machine.
              let fs = sqlIn (ExitPointTests.effectsModel.Force()) "Effects.Wrapped"

              Expect.contains fs (via, literal "sql" "SELECT name FROM users WHERE id = 1") "F# QuerySingleAsync"
              Expect.contains fs (via, literal "sql" "SELECT COUNT(*) FROM sessions") "F# ExecuteScalarAsync"
          }

          // Iteration 8: HTTP request objects.
          test "a request's URL: given to its constructor, or set as RequestUri" {
              let url memberName =
                  (exitOf memberName EffectCategory.HttpOut).Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value)

              Expect.equal (url "RequestConstructed") (literal "url" "https://api.example.org/orders") "constructor"
              Expect.equal (url "RequestProperty") (literal "url" "https://api.example.org/carts/1") "RequestUri"
          } ]
