/// The symbolic interpreter's models of platform behaviour, one at a time: the functions of SymbolicSamples are
/// read back from this test assembly and run over constants or request data.
module Floorplan.Tests.SymbolicTests

open System.IO
open Expecto
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog
open Floorplan.Analysis
open Floorplan.Analysis.Symbolic

type private Marker = class end

let private machine =
    lazy
        (let assembly = typeof<Marker>.Assembly.Location
         let dir = Path.GetDirectoryName assembly
         let runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()

         let searchPaths =
             (Directory.GetFiles(dir, "*.dll") |> Array.toList) @ (Directory.GetFiles(runtime, "*.dll") |> Array.toList)

         let set = new AssemblySet(CecilReader.create (), [ "tests", assembly ], searchPaths)
         let catalog = (CatalogLoader.forTargetFramework "net10.0").Catalog
         let recognizers = [ Floorplan.IL.Codegen.FSharp.FSharpCodegen.recognizer ]
         set, Machine(set, Matcher(catalog, set.Supertypes), recognizers, defaultLimits))

let private sample (name: string) =
    let set, _ = machine.Force()
    let a = (set.TryLoad(Path.GetFileNameWithoutExtension(typeof<Marker>.Assembly.Location))).Value

    a.Types
    |> List.find (fun t -> t.Ref.FullName = "Floorplan.Tests.SymbolicSamples")
    |> fun t -> t.Methods |> List.find (fun m -> m.Ref.Name = name)
    |> fun m -> m.Ref

/// Every outcome of running a sample.
let private run (name: string) (args: SVal list) =
    let _, m = machine.Force()
    m.Invoke(Registration, sample name, args, emptyState, 0)

/// The value a sample returns on every path, which must be one.
let private value (name: string) (args: SVal list) =
    match run name args |> List.map fst |> List.distinct with
    | [ v ] -> v
    | vs -> failwithf "%s: expected one value, got %A" name vs

let private str (s: string) = Const(box s)
let private int (n: int) = Const(box (int64 n))

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Symbolic interpreter models"
        [ test "dict with value-type keys (StructBox) and Dictionary with string keys" {
              Expect.equal (value "dictOfChars" []) (str "second") "dict of chars"
              Expect.equal (value "dictionaryOfStrings" []) (int 4) "Dictionary set_Item, Add and ContainsKey"
          }

          test "a char's ToString is the character" { Expect.equal (value "charText" []) (str "q") "char text" }

          test "a loop with a known end runs to its end" {
              Expect.equal (value "listOfLongString" []) (int 26) "26 characters, past the forked-path loop limit"
          }

          test "interpolated strings over constants fold" {
              Expect.equal (value "interpolated" [ str "world"; int 3 ]) (str "hello world, 3") "interpolation"
          }

          test "Regex.Replace with an evaluator runs on a known input" {
              Expect.equal (value "replaceWithEvaluator" []) (str "/a/<x>/<y>") "replacement"
          }

          test "a match's group count carries the match condition, and a true result returns it" {
              let outcomes = run "groupCount" [ Req(Set.singleton (Taint.Part RequestPart.Path)) ]

              let matched =
                  outcomes
                  |> List.choose (fun (v, _) -> match v with Bool c -> Some c | _ -> None)

              Expect.isNonEmpty matched "the path where the pattern matched returns a condition"

              for c in matched do
                  Expect.exists
                      c.WhenTrue
                      (fun p -> p.Part = RequestPart.Path && p.Operator = PredicateOperator.Matches && p.Value = @"^/orders/(\d+)$")
                      "true exactly when the path matches"

              Expect.exists outcomes (fun (v, s) -> v = int 0 && s.Predicates.IsEmpty) "a path where it did not match"
          }

          test "reflection reads declared parameter names" {
              Expect.equal (value "parameterNameOfHandler" []) (str "orderId") "parameter name"
          }

          test "MethodInvoker calls the reflected method" {
              Expect.equal (value "invokeHandlerByReflection" []) (int 10) "5 + 5 through reflection"
          }

          test "anchored path patterns become route templates" {
              Expect.equal (RouteRecovery.templateOfPattern @"^/api/orders/(-?\d+)$") (Some "/api/orders/{param}") "one group"
              Expect.equal (RouteRecovery.templateOfPattern @"^/a/([^/]+)/b/(\d+)$") (Some "/a/{param1}/b/{param2}") "two groups"
              Expect.equal (RouteRecovery.templateOfPattern @"^/files/a\.txt$") (Some "/files/a.txt") "escaped literal"
              Expect.equal (RouteRecovery.templateOfPattern @"/api/orders/(\d+)") None "not anchored"
              Expect.equal (RouteRecovery.templateOfPattern @"^/api/.*$") None "a wildcard is not a template"
              Expect.equal (RouteRecovery.templateOfPattern @"^/a/((\d)+)$") None "nested groups"
          }

          test "route parameters are named after the handler when the counts match" {
              Expect.equal (RouteRecovery.nameParameters [ "id" ] "/api/orders/{param}") "/api/orders/{id}" "one name"
              Expect.equal (RouteRecovery.nameParameters [ "a"; "b" ] "/x/{param1}/{param2}") "/x/{a}/{b}" "two names"
              Expect.equal (RouteRecovery.nameParameters [ "a"; "b" ] "/x/{param}") "/x/{param}" "counts differ"
              Expect.equal (RouteRecovery.nameParameters [ "tupledArg" ] "/x/{id:int}") "/x/{id:int}" "no placeholders"
          } ]
