module Floorplan.Tests.ILTests

open System.IO
open Expecto
open Floorplan.IL
open Floorplan.IL.Codegen.FSharp

/// An assembly set over the Shop fixture: every project's output, plus its output folder and references.
let shopSet =
    lazy
        (let solution = Fixtures.shop.Force()
         let users = solution.Projects |> List.map (fun p -> p.Id, p.OutputAssembly)

         let search =
             solution.Projects
             |> List.collect (fun p ->
                 (Directory.GetFiles(Path.GetDirectoryName p.OutputAssembly, "*.dll") |> Array.toList) @ p.ReferencePaths)

         new AssemblySet(CecilReader.create (), users, search))

let private method (set: AssemblySet) (assembly: string) (typeName: string) (name: string) =
    let a = (set.TryLoad assembly).Value
    let t = a.Types |> List.find (fun t -> t.Ref.FullName = typeName)
    t.Methods |> List.find (fun m -> m.Ref.Name = name)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "IL"
        [ test "reads user assemblies with sequence points" {
              let set = shopSet.Force()
              let price = method set "Shop.Web" "Shop.Web.Handlers" "price"
              let body = price.Body.Value.Value
              let points = body.Instructions |> Array.choose (fun i -> i.Point)
              Expect.isNonEmpty points "sequence points"
              Expect.isTrue (points |> Array.forall (fun p -> p.File.EndsWith "Handlers.fs")) "points in Handlers.fs"
          }

          test "classifies assembly origins" {
              let set = shopSet.Force()
              Expect.equal (set.Origin "Shop.Core") (User "Shop.Core") "user"
              Expect.equal (set.Origin "FSharp.Core") Dependency "FSharp.Core is a dependency"
              Expect.equal (set.Origin "System.Net.Http") Framework "framework"
          }

          test "data flow finds the producers of a call's arguments" {
              let set = shopSet.Force()
              let price = method set "Shop.Web" "Shop.Web.Handlers" "price"
              let flow = (DataFlow.ofMethod price).Value
              let instrs = price.Body.Value.Value.Instructions

              let call =
                  instrs
                  |> Array.find (fun i ->
                      match i.Op with
                      | CallVirt m -> m.Name = "GetStringAsync"
                      | _ -> false)

              match flow.OperandsAt call.Offset with
              | [ _client; url ] ->
                  let producer =
                      match Set.toList url with
                      | [ DataFlow.Pushed o ] -> (flow.InstructionAt o).Value
                      | other -> failtestf "unexpected producers %A" other

                  match producer.Op with
                  | Call concat when concat.Name = "Concat" ->
                      match flow.OperandsAt producer.Offset with
                      | [ literal; argument ] ->
                          match Set.toList literal with
                          | [ DataFlow.Pushed o ] ->
                              Expect.equal (flow.InstructionAt o).Value.Op (LdStr "https://prices.example.org/items/") "literal part"
                          | other -> failtestf "unexpected %A" other

                          Expect.equal (Set.toList argument) [ DataFlow.Argument 0 ] "argument part"
                      | other -> failtestf "unexpected concat operands %A" other
                  | other -> failtestf "unexpected producer %A" other
              | other -> failtestf "unexpected operands %A" other
          }

          test "the call graph follows closures to their bodies" {
              let set = shopSet.Force()
              let graph = CallGraph(set, [ FSharpCodegen.recognizer ])
              let summarize = method set "Shop.Core" "Shop.Core.Orders" "summarize"
              graph.AddRoots [ summarize.Ref ]

              let reachable = graph.ReachableMethods |> List.map (fun m -> m.Display) |> Set.ofList
              Expect.contains reachable "Shop.Core.Orders/Validation::isValid" "through the List.filter closure"
              Expect.contains reachable "Util::describe" "through the List.map closure"

              let deferred =
                  graph.Callees summarize.Ref |> List.filter (fun e -> e.Kind = Deferred)

              Expect.isNonEmpty deferred "closure construction edges"
          }

          test "method keys of a call site and its definition agree" {
              let set = shopSet.Force()
              let handlers = method set "Shop.Web" "Shop.Web.Handlers" "listOrders"

              let calls =
                  handlers.Body.Value.Value.Instructions
                  |> Array.choose (fun i ->
                      match i.Op with
                      | Call m when m.Type.Assembly = "Shop.Core" || m.Type.Assembly = "Shop.Data" -> Some m
                      | _ -> None)

              Expect.isNonEmpty calls "calls into the solution"

              for c in calls do
                  Expect.isSome (set.TryMethod c) $"resolves {c.Key}"
          } ]
