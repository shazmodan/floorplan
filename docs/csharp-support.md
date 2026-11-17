# C# support: status

Date: 2026-11-17. Plan section 10, Phase 7. Fixtures: `tests/fixtures/Mixed` (F# and C# projects calling each
other), `tests/fixtures/CSharp` (routing in C#) and `Shop.Storage` in `tests/fixtures/Shop`.

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

## Done: entry points and C# compiler output

Fixture: `tests/fixtures/CSharp`, the routing styles of the Routing fixture written in C#, built for net8.0 and
net10.0 with the same golden results (`tests/golden/csharp.entry-points.txt`):

| Project | Routing | Result |
|---|---|---|
| `CSharp.Endpoints` | Minimal APIs: method groups, lambdas, async lambdas, a group handed to a method, a nested group behind `RequireAuthorization`, a template built at run time | Every constant route complete, one entry each; the run-time template partial |
| `CSharp.Controllers` | `[ApiController]` attribute routes and `MapDefaultControllerRoute` | As for F# controllers |
| `CSharp.Middleware` | `==`, `StartsWith`, `HttpMethods.IsOptions`, `Split` with indexing, a `switch` statement on the method | As for the F# middleware |
| `CSharp.Switch` | A switch expression on a `(method, path)` tuple with seven string cases (Roslyn compares hashes, then strings), and list patterns over segments (`["api", "orders", var id]`) | Every case complete, with a path parameter for `var id` |
| `CSharp.Table` | A `Dictionary<string, Func<string>>` initializer behind `HttpListener` | As for the F# table |

Most of what C# emits needed no new code: it is either shared by every .NET compiler (delegates through `ldftn`,
async state machines, iterators) or made of platform calls the analysis reads anyway (`String.op_Equality` in
string switches, array length and element access in list patterns, `string.Format`).
`Floorplan.IL.Codegen.CSharp` holds what is Roslyn's own:

- **Delegate caches.** Roslyn caches the delegate for a lambda (`<>c.<>9__0_1`) or a static method group
  (`<>O.<0>__Name`) in a static field and creates it only when the field is null. The symbolic run read such a
  field as unknown, so every minimal API route had a second entry whose handler was "a library function". Those
  fields now start out null (`ICodegenRecognizer.IsLazyCache`).

Also new, outside the recognizer:

- **Interpolated strings** built with the platform's `DefaultInterpolatedStringHandler` are read as payload
  templates (`$"orders/{id}.json"` gives `orders/{…}.json`), as `string.Format` already was.
- **Readable C# names on the website**: a lambda is shown by its enclosing method (`Admin › lambda in Map`,
  `Program › lambda in top-level statements`), an async body as `Store › async body of Load`, a local function by
  its name. The compiler's name stays in the tooltip.
- The global namespace unit is shown by its label wherever the site names a unit.

## Done: effects, dependencies, source generators

- **`CSharp.Effects`** ports the F# Effects fixture (`tests/golden/csharp.exit-points.txt`): every category
  called directly, ADO.NET through SQLite and Npgsql (using declarations, an object initializer), Dapper and
  Flurl as wrapper libraries, a lambda capturing a local, an interface, an implementation only named in a
  dependency-injection registration, an async method, an iterator, literal/constant/template/dynamic payloads,
  `LibraryImport`, and reflection as a blind spot. All give the F# results.
- **`CSharp.Delegated`** with `Dependency/CSharp.Library`: the middleware router in a library referenced as a
  built assembly (a dependency, like a package), with the application's handlers passed as method groups in a
  record. Same routes as the F# `Routing.Delegated`.
- **Source generators** run during structure analysis: the generator assemblies the compiler is given
  (`/analyzer:`) are loaded, one load context per folder (projects on different .NET versions have generators of
  the same name), and their output is compiled so that code calling it binds. Without this, every `LibraryImport`,
  `[GeneratedRegex]`, JSON or logging generator gave a compile error.
- **Types of a namespace**: a namespace unit lists the types it declares (`CodeUnit.Types`, shown on its page).
- **SQL text** (F# and C#): an ADO.NET command's `CommandText` is the payload of its `Execute*` calls, whether
  set as a property, in an object initializer or through the constructor; built from values it is a template
  (`DELETE FROM {…}`), and a command made elsewhere stays dynamic. A request's URL is read the same way
  (`RequestUri`, or the URL given to `new HttpRequestMessage(method, url)`).
- **Payloads through libraries** (F# and C#, `docs/library-payloads-plan.md`): for an effect behind a dependency,
  the payload is worked out from the dependency's own code, in terms of its arguments, and evaluated at the call
  site; no rule names the library. Arguments passed on, text built from them, properties and fields of objects
  (set by setters, constructors, or methods that return the object), structs passed by reference, async methods,
  iterators and F# `task { }` / `seq { }` are followed. Dapper's `Query` and `Execute` show their SQL; when a
  library reaches the primitive by several routes, the shortest one whose payload is known is shown, with its
  evidence. Values kept in collections or caches, or rebuilt from parsed objects (Flurl's `Url`), stay dynamic.
  Fixtures: `Dependency/CSharp.Wrappers` and `Effects/Dependency/Effects.Wrappers`, both built optimized as
  packages are.
- **Route parameter names** (F# and C#): a segment taken as a parameter is named after the handler parameter
  that receives it (`/orders/{id}` for `Show(HttpContext ctx, string id)`), for control-flow routes as it already
  was for platform routes.

## Not done yet

- Generators that read additional files or MSBuild properties through analyzer config options get none, so code
  depending on what those generate may not bind; it is then reported as a compile error and the result is not
  cached.
