/// Payloads of effects behind a library, worked out from the library's code (docs/library-payloads-plan.md).
/// The fixture is CSharp.Effects calling Dependency/CSharp.Wrappers. A pending test is enabled by the iteration
/// that makes it pass; the plan names which.
module Floorplan.Tests.LibraryPayloadTests

open Expecto
open Floorplan.Model

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
          ptest "a: the argument is the command text" { Expect.equal (sql "ExecLiteral") (literal "sql" "DELETE FROM sessions") "" }

          ptest "b: the argument is the URL" {
              let url = (exitOf "FetchLiteral" EffectCategory.HttpOut).Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value)
              Expect.equal url (literal "url" "https://api.example.org/status") ""
          }

          ptest "c: passed on through another library call" { Expect.equal (sql "RunChain") (literal "sql" "DELETE FROM carts") "" }

          ptest "i: through a dictionary the SQL is not followed" {
              Expect.equal (sql "ThroughDictionary") (Some("sql", PayloadKind.Dynamic, None)) ""
          }

          // Iteration 3: values built inside the library.
          ptest "d: built inside the library from a literal argument" {
              Expect.equal (sql "CountLiteral") (literal "sql" "SELECT count(*) FROM orders") ""
          }

          ptest "d: built inside the library from an unknown argument" {
              Expect.equal (sql "CountDynamic") (template "sql" "SELECT count(*) FROM {…}") ""
          }

          ptest "j: an interpolated argument is a template" {
              Expect.equal (sql "Interpolated") (template "sql" "DELETE FROM orders WHERE id = {…}") ""
          }

          // Iteration 4: properties, fields, constructors, returned objects.
          ptest "e: the command is made and returned by another library method" {
              Expect.equal (sql "ScalarReturned") (literal "sql" "SELECT max(id) FROM orders") ""
          }

          ptest "f: kept in an object the caller made" {
              Expect.equal (sql "ExecuteQueryObject") (literal "sql" "UPDATE orders SET seen = 1") ""
          }

          ptest "k: kept in a struct the library made, passed by reference" {
              Expect.equal (sql "ExecuteDefinition") (literal "sql" "DELETE FROM logs") ""
          }

          // Iteration 5: state machines.
          ptest "g: an async library method" { Expect.equal (sql "ExecAsyncLiteral") (literal "sql" "DELETE FROM tokens") "" }

          ptest "h: an iterator library method" { Expect.equal (sql "RowsLiteral") (literal "sql" "SELECT name FROM users") "" } ]
