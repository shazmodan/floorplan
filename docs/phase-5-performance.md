# Phase 5: traces, noise control and caching

Date: 2026-11-15.

## Traces

Every entry point has a trace (plan section 6.8): the exit points reachable from its handler through user code,
across projects. The website shows them in the entry-point table ("Reaches") and on the evidence page.

## Noise control

- **Confidence** on every exit point (plan risk 2). Direct calls to a primitive are high. Paths through a
  dependency score one per call and one more per virtual call: up to 4 is high, up to 12 medium, beyond that low.
- **Presentation defaults in the model.** Environment and nondeterminism are hidden by default, and so are
  low-confidence results; the website has toggles for both. `--show environment,nondeterminism` changes the
  defaults written into the model, so the renderer still reads nothing but the model.

On the four-project solution reviewed in Phase 2, 362 of 417 exit points are low confidence (deep error paths
inside the F# compiler service) and are hidden by default; the 55 shown are the useful ones.

## Caching

Both caches live in `FLOORPLAN_CACHE`, by default the user's local application data folder; `--no-cache`
turns them off.

| Cache | Key | Effect on the four-project solution |
|---|---|---|
| Structure, per project (plan risk 9) | Sources and their contents, compiler options, referenced assemblies (size and time), and the assembly-to-project map | Structure analysis 14 s → 0.2 s |
| Dependency summaries, per assembly, catalog and reference closure (plan section 5) | Assembly MVID and the names of the assemblies in the project's reference closure; valid while every assembly its paths pass through keeps its MVID | Effect analysis 20 s → 11 s |

Whole run: 40 s cold, 18 s warm, of which about 6 s is the build check and MSBuild evaluation.

The summary cache stores one next hop per method and rule, and evidence paths are rebuilt by following the
hops. A warm run does not explore cached dependencies; if user code calls a dependency method the cache does
not cover, the run falls back to a full analysis.

Summaries are made per catalog and reference closure (2026-11-23): a virtual call inside a dependency reaches
only implementations in types that code in the project's closure constructs, as the project's view of the call
graph already did for its own calls. Made once for the whole solution, they let a project's Serilog log calls
reach the Console sink and a sink library that only another project of the fixture solution referenced. Each
summary covers the dependencies in its closure only, so the 21 projects of `CSharp.slnx` take 8 s together where
the one solution-wide summary took 7.4 s. An assembly is cached when every closure that includes it has an entry.

## Assembly resolution

One assembly set serves the solution: an assembly is read from one file, whichever projects use it. Since
2026-11-23 the file is the one a project's build resolved, as far as one file can be:

- A package's copy wins over a shared framework's: a project whose build resolved a newer package
  (OpenTelemetry brings `Microsoft.Extensions.DependencyInjection.Abstractions` 9 into an ASP.NET Core 8
  application, `Microsoft.Extensions.Hosting` comes as a package to a worker) runs that copy.
- Of two packages' copies, the higher version wins (assembly version, then file version: AWS keeps the assembly
  version at 3.3.0.0 across releases). It keeps the older one's members, so every project's calls resolve,
  whatever the order of the projects. Before, the first project's copy won, and a worker on a newer
  `Microsoft.Extensions.Hosting` than another project lost every effect it reached through it.
- Whether a project follows such an assembly's code is the project's own: for a project whose build resolved
  the framework's copy, a package's copy of a framework assembly is framework code, never summarized, as before.
  The symbolic interpreter never runs an assembly a shared framework also ships.

Analyzed alone with `--include`, every project of `CSharp.slnx` now has the effects it has in the solution.
Three keep a different evidence path (one of them one confidence level lower) because they read a newer copy
another project brings.

Known difference: when several shortest paths exist, a warm run may rebuild a different one than the cold run
found, which can change an exit point's confidence by one level. The exit points themselves are the same.
