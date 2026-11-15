# Floorplan

Floorplan is a code visualizer for .NET solutions, F# first. Rooms are the modules, the openings between
rooms are the dependencies, and the doors to the outside are the entry and exit points.

```
floorplan analyze MyApp.sln
```

writes a static website to `./floorplan-out` that shows:

1. **The module graph**: which modules depend on which, grouped by project, with a project graph above it.
2. **Entry points**: inbound HTTP routes and the module that handles each one, with how complete each route is.
3. **Exit points**: outbound HTTP, database, file, process and other side-effects, the module each belongs to,
   and the dependency it goes through.
4. **Traces**: the side-effects each entry point can cause.

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
      [-o|--output <dir>]            default ./floorplan-out
      [--include <project,...>]      analyze only these projects and what they reference
      [--exclude <project,...>]
      [--include-tests]              test projects are excluded by default
      [-f|--framework <tfm>]         preferred target framework for multi-targeting projects
      [-c|--configuration <name>]    default Debug
      [--format site|json]           default site; json writes the analysis model
      [--single-file]                one self-contained HTML file
      [--catalog <file>]             additional catalog rules, e.g. an in-house library as a boundary
      [--show <category,...>]        include categories hidden by default (environment, nondeterminism)
      [--no-build]
      [--no-cache]                   do not use the analysis cache
      [--quiet]
```

The website is plain HTML, CSS and JavaScript with no network access; it opens by double-clicking `index.html`.
The exit code is 1 when some project could not be analyzed; the others are still reported.

Results are cached per project and per dependency assembly in `FLOORPLAN_CACHE` (by default the local
application data folder), so a second run is about twice as fast.

## What is found, and how complete it is

| Result | How |
|---|---|
| Module graph | F# compiler service: every symbol use is an edge from the enclosing to the declaring module |
| Exit points | Call graph over IL (rapid type analysis, closures, async and task state machines, iterators) matched against the catalog; effects inside NuGet packages are summarized and reported at the user call site |
| Entry points | Handlers from platform registrations, from handler tables, and from request-data tracking (a user function called on a branch that request data decides) |
| Routes | Recovered from the conditions on request data: equality, prefixes, constant regexes, path segments, list patterns, active patterns, table keys. Reported as complete, partial or unresolved; never guessed |

Blind spots (reflection, run-time code generation, `dynamic`) are listed under Diagnostics. C# projects appear as
a single node with limited support; their effects are found and attributed to the project.

## Documentation

- [Implementation plan](floorplan-implementation-plan.md)
- [Phase 0 spike findings](docs/phase-0-spike-findings.md)
- [Exit points reviewed on a real solution](docs/phase-2-review.md)
- [Route recovery status and limits](docs/phase-4-route-recovery.md)
- [Traces, noise control and caching](docs/phase-5-performance.md)
- [Updating the catalog for a new .NET version](docs/updating-catalogs.md)

## Building from source

```
dotnet build Floorplan.slnx
dotnet run --project tests/Floorplan.Tests
dotnet run --project src/Floorplan.Cli -- analyze tests/fixtures/Routing/Routing.slnx
```

Requires the .NET 10 SDK; the test fixtures also need the .NET 8 targeting pack. Golden files under
`tests/golden` change only on purpose: set `FLOORPLAN_UPDATE_GOLDEN=1` to rewrite them.

| Folder | Contents |
|---|---|
| `src/Floorplan.Model` | The analysis model every renderer reads |
| `src/Floorplan.ProjectLoading` | Solution loading through MSBuild |
| `src/Floorplan.Structure`, `.Structure.FSharp` | Structure analysis behind a language-neutral interface |
| `src/Floorplan.IL`, `.IL.Codegen.FSharp` | IL reading, data flow, control flow, call graph; F# compiler-output recognizers |
| `src/Floorplan.Catalog` | Boundary catalogs and matching |
| `src/Floorplan.Analysis` | Exit points, entry points, routes, traces |
| `src/Floorplan.Layout` | Graph layout |
| `src/Floorplan.Rendering` | Website and JSON renderers |
| `src/Floorplan.Cli` | The `floorplan` dotnet tool |
| `tools/CatalogDiff` | Catalog maintenance |
| `tests/fixtures` | Small solutions, one per routing and effect style |
