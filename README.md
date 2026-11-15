# Floorplan

Floorplan is a code visualizer for .NET solutions, F# first. Rooms are the modules, the openings between
rooms are the dependencies, and the doors to the outside are the entry and exit points.

```
floorplan analyze MyApp.sln
```

writes a static website to `./floorplan-out` that shows:

1. **The module graph**: which modules depend on which, grouped by project.
2. **Entry points**: inbound HTTP routes and the module that handles each one.
3. **Exit points**: outbound HTTP, database, file, process and other side-effects, and the module each belongs to.

Floorplan knows no web framework. It follows compiled code, through any framework, down to the .NET
primitives that actually touch the network, the disk or a database. See
[floorplan-implementation-plan.md](floorplan-implementation-plan.md) for the design.

## Usage

```
floorplan analyze [<path/to/App.sln>]    default: the solution in the current directory
      [-o|--output <dir>]            default ./floorplan-out
      [--include <project,...>]      analyze only these projects and what they reference
      [--exclude <project,...>]
      [--include-tests]              test projects are excluded by default
      [-f|--framework <tfm>]         preferred target framework for multi-targeting projects
      [-c|--configuration <name>]    default Debug
      [--format site|json]           default site
      [--single-file]
      [--catalog <file>]             additional catalog rules
      [--show <category,...>]        include categories hidden by default
      [--no-build]
```

The solution must build: Floorplan runs `dotnet build` (Debug) first and reads the built assemblies.
The website is plain HTML, CSS and JavaScript and opens by double-clicking `index.html`.

## Building from source

```
dotnet build Floorplan.slnx
dotnet run --project tests/Floorplan.Tests
dotnet run --project src/Floorplan.Cli -- analyze tests/fixtures/Shop/Shop.slnx
```

Requires the .NET 10 SDK; the test fixtures also need the .NET 8 targeting pack.
