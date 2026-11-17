# C# support: status

Date: 2026-11-17. Plan section 10, Phase 7. Fixtures: `tests/fixtures/Mixed` (F# and C# projects calling each
other) and `Shop.Storage` in `tests/fixtures/Shop`.

## Done: structure analysis

`Floorplan.Structure.CSharp` is the Roslyn-based `IStructureAnalyzer` (plan section 3.5). C# projects now have full
support instead of appearing as one limited-support node.

- **Units are namespaces** (open decision 1 in the plan, decided 2026-11-17). Each namespace that declares a
  type, delegate or top-level statement in the project's own source is a unit of kind `namespace`. Types declared
  outside any namespace, and top-level statements, form the project's global namespace unit (empty name, shown as
  "(global namespace)"). A namespace that only contains other namespaces is not a unit. Generated files under
  `obj/` (global usings, assembly info) are compiled but give no units and no edges.
- **Edges**: every name in the source that Roslyn binds to a type or member (including attributes and target-typed
  `new()`) is an edge from the namespace the code is in to the declaring type's unit. `using` directives give no
  edges: they name namespaces and may be unused.
- **Across languages**: an analyzer names an edge's target by what declares the symbol (a C# type, an F# module).
  Structure analysis resolves that name to the innermost unit that contains it, falling back to the target project's
  global namespace. So C# code that calls an F# module gets an edge to that module, and F# code that uses a C# type
  gets an edge to its namespace.
- **Cycles** between namespaces are common in C#. The layout already collapses them into one node (`Billing ⇄
  Storage` in the Mixed fixture).
- **Effects** in C# code are attributed by PDB sequence points and, where a namespace's code spans several files,
  by the declaring type's name. Types in no namespace belong to the global namespace unit.
- The compilation is built from the MSBuild evaluation (compile items, resolved references, `DefineConstants`),
  not from a workspace, and is cached per project like the F# results. Compile errors are reported as warnings
  (`structure.compile-error`) and leave the result uncached.

The architecture test now also keeps Roslyn and `*.CSharp` projects out of `Floorplan.Analysis`,
`Floorplan.Layout` and `Floorplan.Rendering`.

## Not done yet

- **C# compiler-output recognizers** (`Floorplan.IL.Codegen.CSharp`): the recognizer for an assembly is chosen by
  language, and only the F# one exists. C# lambdas and async methods are already linked through what every
  compiler shares (delegates created with `ldftn`, `IAsyncStateMachine`), so exit points in C# code are found,
  including one in a lambda inside an async method. But route recovery in C# code lacks C#'s idioms: string
  `switch` (hash and compare), string equality as C# emits it, collection access, and `string.Format` and
  interpolation handlers for payload templates. Lambdas keep their compiler names
  (`InvoiceStore/<>c::<ArchiveAsync>b__1_0`) where F# ones are made readable.
- **C# entry-point fixtures**: minimal APIs and controllers written in C#, and hand-written routing in C#, with
  golden files, so that C# and mixed solutions pass the same fixture categories as F# (the Phase 7 "done when").
- A unit lists no types: the unit detail page shows a namespace's first declaration, not the types it holds.
