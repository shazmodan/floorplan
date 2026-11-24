# Floorplan

Floorplan is a code visualizer for .NET solutions, F# first. Rooms are the modules, the openings between
rooms are the dependencies, and the doors to the outside are the entry and exit points.

```
floorplan analyze MyApp.sln
```

writes a static website to `./floorplan-out` that shows:

1. **The module graph**: which modules depend on which, grouped by project, with a project graph above it.
   Dependencies between projects are drawn once per pair of projects (select a module to see its own), and
   modules that use no other module and are used by none are set out in their own section below the graph.
2. **The flow**: the module graph cut down to where requests enter and side-effects leave. Each module that
   handles requests carries a card of its routes, each module with side-effects a card per kind (database, file,
   outbound HTTP...), and the modules between them are drawn with their dependencies.
3. **Entry points**: inbound HTTP routes and the module that handles each one, with how complete each route is;
   messages a queue delivers; the background work a host, a timer or a channel starts; and, for an application
   nothing else starts (a command-line tool), where the process starts.
4. **Exit points**: outbound HTTP, database, file, process, messages sent and other side-effects, the module each
   belongs to, and the dependency it goes through.
5. **Traces**: the side-effects each entry point can cause.
6. **Architecture**: a [C4 model](https://c4model.com) of the solution in its four levels, derived from the
   above: the system with its users and the external systems it reaches, its applications (containers), each
   application's components (the modules of its own project and of the libraries it references) and each
   component's code (its types, and which use which). Each element opens the level below, and each relationship lists the entry and
   exit points behind it. A sidebar lists every view (filtered by name), a switcher moves between the four
   levels, and keys move too: 1 to 4 for a level, u for up, v for the sidebar, / to filter. The site's folder also holds the model as a [Structurizr DSL](https://docs.structurizr.com/dsl)
   workspace, `workspace.dsl`, to open in Structurizr or export to PlantUML or Mermaid with its tools; `workspace.json`
   beside it holds the website's layout, which Structurizr Lite merges in.

Floorplan knows no web framework. It reads the compiled code of the solution and its dependencies and follows it
down to the .NET primitives that actually touch the network, the disk or a database. A versioned, data-driven
[boundary catalog](src/Floorplan.Catalog/catalogs) says which primitives those are. Every result keeps its
evidence (the call path or the conditions behind it), so it can be checked by hand. See
[floorplan-implementation-plan.md](floorplan-implementation-plan.md) for the design.

## Install

```
dotnet pack src/Floorplan.Cli -c Release -o nupkg
dotnet tool install --global Floorplan --add-source ./nupkg
```

Floorplan runs on the newest installed .NET runtime (10 or later) and analyzes projects that target .NET 8 and
later. The solution must build: Floorplan runs `dotnet build` (Debug) first and reads the built assemblies and
their PDBs.

## Usage

```
floorplan analyze [<path/to/App.sln>]    default: the solution in the current directory
floorplan analyze <path/to/folder>       a folder of solutions, analyzed as one landscape (below)
      [-o|--output <dir>]            default ./floorplan-out
      [--include <project,...>]      analyze only these projects and what they reference
      [--exclude <project,...>]
      [--solutions <name,...>]       in a folder of solutions: only these, nested ones too
      [--exclude-solutions <name,...>]
      [--include-tests]              test projects are excluded by default
      [-f|--framework <tfm>]         preferred target framework for multi-targeting projects
      [-c|--configuration <name>]    default Debug
      [--format site|json]           default site; json writes the analysis model
      [--single-file]                one self-contained HTML file (without workspace.dsl)
      [--catalog <file>]             additional catalog rules, e.g. an in-house library as a boundary
      [--show <category,...>]        include categories hidden by default (environment, nondeterminism)
      [--no-build]
      [--no-cache]                   do not use the analysis cache
      [--quiet]                      print only errors and the path of the output
      [--silent]                     print nothing; the exit code says how the run went
```

The website is plain HTML, CSS and JavaScript with no network access; it opens by double-clicking `index.html`.
The exit code is 1 when some project could not be analyzed; the others are still reported.

While it runs, Floorplan writes each step to stderr with the time since the start: each project built, loaded,
compiled and analyzed. On a terminal, a spinner line under the steps shows how long the current one has taken.
`--quiet` writes only errors and the path of the output, `--silent` nothing at all.

### A folder of solutions

`floorplan analyze C:\Projects`, where `C:\Projects` has no solution of its own but folders that do
(`SystemA\SystemA.sln`, `SystemB\SystemB.slnx`), analyzes each solution as a system of one landscape
([docs/landscape-plan.md](docs/landscape-plan.md)). Build output, `node_modules` and hidden folders are skipped,
and a solution inside another solution's folder (a `samples` solution) belongs to that system: it is listed, and
analyzed only when named with `--solutions`. The other options apply to every solution; a solution that cannot
be analyzed is listed with why, and the others still are.

Each solution's site is written to `solutions/<name>/`, with a way back and a switcher to the other solutions.
The landscape's site beside them has:

- **Solutions**: each solution with its applications, projects, entry and exit points, and how many calls it makes
  to the others and receives from them.
- **Landscape**: a C4 model of the whole: every system with its users and the external systems more than one of
  them uses (or every external system), every system's applications in a boundary each, and each system's
  applications with those of other systems they talk to. An application opens its solution's site.
- **Interactions**: every call one system makes that another receives, from which member to which handler, what
  (route, queue), and how it was matched, with links to the evidence in both sites.
- **Diagnostics** of every solution.

A call is matched to another system when the code says so: a message to a queue or topic the other system
consumes through the same library (also gRPC, WCF and Orleans calls); an HTTP call to one of the other system's
routes whose URL's host names that system or one of its applications (`billing-api.internal` names Billing); or a
call to a route only one other system has, which is drawn dashed as probable, since two deployments can run the
same route. For hosts that name nothing, `floorplan.landscape.json` in the analyzed folder can say which system
they are: `{ "hosts": { "api.contoso.com": "SystemB" } }`. A folder (an absolute path) that more than one system
uses is shown as one element they share. In each solution's own site, a system it talks to is shown as that
system, opening its site, rather than as the host it is called at. `workspace.dsl` beside the landscape's site
holds the landscape as a Structurizr workspace, its probable relationships dashed.

Results are cached per project and per dependency assembly in `FLOORPLAN_CACHE` (by default the local
application data folder), so a second run is about twice as fast.

## What is found, and how complete it is

| Result | How |
|---|---|
| Module graph | F# compiler service for F# (a unit per module), Roslyn for C# (a unit per namespace): every symbol use is an edge from the enclosing to the declaring unit, also between F# and C# projects |
| Exit points | Call graph over IL (rapid type analysis, closures, async and task state machines, iterators) matched against the catalog; effects inside NuGet packages are summarized and reported at the user call site |
| Entry points | Handlers from platform registrations, from handler tables, and from request-data tracking (a user function called on a branch that request data decides); read from declarations where the platform finds them by reflection (MVC controllers, Razor Pages, Blazor components, SignalR hubs, gRPC services, Azure Functions) |
| Other inputs | Message consumers (RabbitMQ, Azure Service Bus, MassTransit, NServiceBus, Kafka, SQS, Redis, a SignalR client, Dapr subscriptions), Orleans grain methods at their interface and method, WCF operations (CoreWCF) at their action, background work (hosted services, timers, Quartz.NET and Hangfire jobs, channel readers and queued work items), file watchers at their path, and a command-line program's start |
| Outputs through client libraries | Where a library sends from loops or pipelines of its own, the catalog names the calls: Entity Framework Core, Redis, MongoDB, Cosmos DB, DynamoDB, S3, Azure Blob Storage, Refit and gRPC clients, Dapr's building blocks, calls to Orleans grains, WCF clients, OpenTelemetry exporters, message publishing, email (MailKit, SendGrid); NLog, log4net and Serilog targets named in configuration files |
| Architecture (C4) | Derived from the results above, with no list of products: an external system is named after a URL's host or the namespace of the client library that reaches it; applications that send to a queue another consumes, or call another's route on a local host, are linked (`docs/c4-plan.md`) |
| Routes | Recovered from the conditions on request data: equality, prefixes, constant regexes, path segments, list patterns, active patterns, table keys. Reported as complete, partial or unresolved; never guessed |

Blind spots (reflection, run-time code generation, `dynamic`) are listed under Diagnostics. C# projects get
namespaces, dependencies, entry points and exit points, with the same results as F# on the same fixture
categories: minimal APIs, controllers, hand-written routing, switch expressions, list patterns, tables, routing in
a dependency, and every effect category ([status](docs/csharp-support.md)). Projects in other languages appear as
a single node with limited support; their effects are found and attributed to the project.

## Documentation

- [Implementation plan](floorplan-implementation-plan.md)
- [Phase 0 spike findings](docs/phase-0-spike-findings.md)
- [Exit points reviewed on a real solution](docs/phase-2-review.md)
- [Route recovery status and limits](docs/phase-4-route-recovery.md)
- [Routing inside frameworks (Giraffe, Falco, Oxpecker fixtures)](docs/framework-routing.md)
- [Traces, noise control and caching](docs/phase-5-performance.md)
- [Updating the catalog for a new .NET version](docs/updating-catalogs.md)
- [C# support status](docs/csharp-support.md)

## Building from source

```
dotnet build Floorplan.slnx
dotnet run --project tests/Floorplan.Tests
dotnet run --project src/Floorplan.Cli -- analyze tests/fixtures/Routing/Routing.slnx
```

Requires the .NET 10 SDK; the test fixtures also need the .NET 8 targeting pack. The fixture applications
target both net8.0 and net10.0, and the tests check that both give the same golden results (Shop mixes the two
on purpose; the Oxpecker fixture is net10.0 only, as its package is). Golden files under `tests/golden` change
only on purpose: set `FLOORPLAN_UPDATE_GOLDEN=1` to rewrite them.

| Folder | Contents |
|---|---|
| `src/Floorplan.Model` | The analysis model every renderer reads |
| `src/Floorplan.ProjectLoading` | Solution loading through MSBuild |
| `src/Floorplan.Structure`, `.Structure.FSharp`, `.Structure.CSharp` | Structure analysis behind a language-neutral interface |
| `src/Floorplan.IL`, `.IL.Codegen.FSharp`, `.IL.Codegen.CSharp` | IL reading, data flow, control flow, call graph; F# and C# compiler-output recognizers |
| `src/Floorplan.Catalog` | Boundary catalogs and matching |
| `src/Floorplan.Analysis` | Exit points, entry points, routes, traces |
| `src/Floorplan.Layout` | Graph layout |
| `src/Floorplan.Rendering` | Website and JSON renderers |
| `src/Floorplan.Cli` | The `floorplan` dotnet tool |
| `tools/CatalogDiff` | Catalog maintenance |
| `tests/fixtures` | Small solutions, one per routing and effect style |
