# C4 model of an analyzed solution: plan

Date: 2026-11-21. Status: in progress. Adds a C4 model (https://c4model.com) of the analyzed solution to the
analysis model, and shows it on the website.

## Resuming work (read this first)

This plan is worked in iterations. Each one is a branch of its own, ends with every test green, and is merged on
its own. To pick up after a break:

1. Read the **Status** table below. The first iteration not marked done is the current one.
2. Check `git branch -a` for that iteration's branch. If it exists, check it out and read its commits and the
   iteration's **Log** entry: that is where work stopped. If not, branch from `main` with the name in the table.
3. Do the iteration's steps, then its **Done when** checks and the whole suite:
   `dotnet run --project tests/Floorplan.Tests -- --summary`. Goldens are updated with `FLOORPLAN_UPDATE_GOLDEN=1`;
   check every changed line.
4. Before committing, update this file in the same commit: the Status row, and a dated **Log** entry with what was
   done, what was found, and anything left over. Findings that change the design go into **Design** as well.
5. The owner asked (2026-11-21) for no pull requests: when the whole suite passes locally, merge into `main`
   with `--no-ff`, push, delete the branch, and go on to the next iteration.

Never leave knowledge only in a conversation: if a session ends mid-iteration, the Log entry must say where it
stopped.

## Status

| # | Iteration | Branch | Status |
|---|---|---|---|
| 1 | Plan, model types, an empty model in every run | `claude/c4-model-plan` | done |
| 2 | Level 1, system context: the system, its users and the external systems | `claude/c4-system-context` | done |
| 3 | Level 2, containers: applications, libraries, and links between applications | `claude/c4-containers` | done |
| 4 | Level 3, components: the units of each container | `claude/c4-components` | done |
| 5 | Level 4, code: the types and members of each component | `claude/c4-code` | |
| 6 | Diagram layout of every view | `claude/c4-layout` | |
| 7 | The website's Architecture pages | `claude/c4-website` | |
| 8 | Documentation, TODO, review on every fixture | `claude/c4-docs` | |

## Goal

Every run gives, beside what it gives today, a C4 model of the solution in its four levels, derived from what the
analysis already found, with nothing configured by hand:

1. **System context**: the solution as one software system, the people and systems that call it (from the entry
   points), and the external systems it uses (from the exit points).
2. **Containers**: the applications of the solution (each project that is an application), what each one is
   (a web application, a worker, a console application), the external systems each one talks to, and the
   applications that talk to each other (one sends to a queue another consumes).
3. **Components**: for each container, the units (namespaces, F# modules) of its own project and of the library
   projects it references, the dependencies between them, and which ones handle requests or reach out.
4. **Code**: for each component, its types and the members that are entry or exit points.

Each element and relationship keeps references to what it was derived from (entry points, exit points, units,
dependencies), so it can be checked on the existing pages, as every other result can.

The website gets an **Architecture** page: the four levels as diagrams in C4's notation, each element linking down
to the next level (a container opens its components, a component its code) and to the existing unit and evidence
pages. The JSON output carries the model too.

Not goals: hand-written C4 models or merging with them, deployment diagrams, dynamic diagrams, exporting to other
C4 tools (Structurizr DSL, PlantUML). Possible later.

## Where things are today

- `src/Floorplan.Model/Model.fs`: the analysis model. `Project` (with `IsApplication` and `ProjectReferences`),
  `CodeUnit` (namespace, module or project unit, with `Types` for namespaces), `Dependency` between units,
  `EntryPoint` (category, application, handler unit and member, route) and `ExitPoint` (category, unit, member,
  primitive, payload, `ViaAssembly`).
- `src/Floorplan.Layout`: `Layered.layout` lays out a directed graph (cycles collapsed), `SolutionLayout` the
  project, unit and flow graphs. All layout is computed in the core so every renderer draws the same picture.
- `src/Floorplan.Cli/Pipeline.fs`: `buildModel` runs the stages and assembles the model.
- `src/Floorplan.Rendering/site/app.js`: the website, one page per hash route (`#/units`, `#/flow`, ...), drawing
  SVG from the layouts in the model.
- Goldens in `tests/golden` are text, one line per result, written by `tests/Floorplan.Tests/Golden.fs`.

## Design

### Model (`Floorplan.Model`, in `AnalysisModel.Architecture`)

- `C4Element`: `Id`, `Kind` (person, software system, external system, container, component, code), `Name`,
  `Technology` (e.g. "C# · net8.0 · web application"), `Description`, `Parent` (the element it is part of),
  `Tags` (e.g. "library", "handles requests", "has side-effects"), and `Sources`: the ids of the projects, units,
  entry and exit points it was derived from.
- `C4Relationship`: `From`, `To`, `Description` (e.g. "GET /api/orders", "reads and writes", "sends to queue
  orders"), `Technology` (e.g. "HTTP", "RabbitMQ"), and `Sources`.
- `C4View`: `Id`, `Level` (context, containers, components, code), `Scope` (the element the view is of: the
  system, a container, a component), the element ids it shows, the relationships between them (iteration 3: the
  model's relationships lifted to the shown elements that contain their ends, and merged, so a renderer draws a
  view without knowing the model's hierarchy), and its `Layout` (a `GraphLayout`, iteration 6).
- `C4Model`: elements, relationships, views. A relationship is stored between the most specific elements
  (a code element and an external system); a view shows it between the elements it shows that contain them, so
  the levels agree with one another by construction.
- The schema version goes to 2.

### Deriving the levels (`Floorplan.Analysis` or a new `Floorplan.Architecture` project, decided in iteration 2)

- **External systems** come from exit points, grouped by what they reach, named without knowing any library:
  - outbound HTTP: the host of the payload's URL when it is absolute (`prices.example.org`), else one "HTTP
    services" element for calls whose host is unknown;
  - database, network and messaging: the library the primitive belongs to (the first segments of the
    primitive's namespace, e.g. `StackExchange.Redis`, `RabbitMQ.Client`), or for the platform's own primitives
    (`System.Data`, `System.Net.Sockets`) the kind ("SQL database through ADO.NET", "network");
  - files: one "file system"; processes: "operating system processes"; native code: "native libraries";
  - categories hidden by default (environment, nondeterminism) and low-confidence exit points are left out, as
    the website leaves them out by default.
- **People and calling systems** come from entry points: HTTP routes are called by a "web client" person;
  messages come from the external system that delivers them (named as for exit points, from the registration's
  evidence); background work is started by the application itself and has no caller.
- **Containers** are the application projects. A library project is not a container: its units appear as
  components in every container that references it, tagged as part of that library. A library no application
  references is a container of its own, tagged "library".
- **Between containers**: a message sent to a queue (an exit point's payload) that another application consumes
  (an entry point's route), and an HTTP call whose URL path matches another application's route. Matches are
  exact; nothing is guessed.
- **Components** are units; dependencies between units are relationships between components.
- **Code** elements are the types a unit declares (`CodeUnit.Types`) and the types of the members that are entry
  or exit points (from `HandlerMember` and `Member`, "Namespace.Type::Method"). An F# module is one code element.

### Website

- `#/architecture` shows the system context; `#/architecture/containers`, `#/architecture/container/<id>` and
  `#/architecture/component/<id>` the other levels, with a breadcrumb. Diagrams use C4's notation: a person
  shape, boxes with the name, `[kind: technology]` and description, external elements in grey, the scope's
  boundary drawn around its parts, labelled relationship arrows, and a legend.
- Clicking an element opens its level below (a container its components, a component its code), and a side
  panel links to the existing pages (unit, evidence of each entry and exit point).

## Log

### 2026-11-21: iteration 1

Plan written. `C4Kind`, `C4Element`, `C4Relationship`, `C4Level`, `C4View` and `C4Model` are in
`Floorplan.Model`, and `AnalysisModel.Architecture` holds the model; the schema version is 2. The derivation is a
project of its own, `src/Floorplan.Architecture` (`C4.derive : AnalysisModel -> C4Model`), because it needs
nothing but the finished analysis model: no IL, no compiler. It depends on `Floorplan.Model` and
`Floorplan.Layout`, and is checked as language-neutral by the architecture tests. `Pipeline.buildModel` derives
the model last ("Deriving the C4 model"); for now `derive` returns an empty one. The JSON round-trip test covers
the new types.

### 2026-11-21: iteration 2

`C4.derive` gives the system context: the solution as the software system (`system`), a `person:user` for HTTP
routes, and an external system per thing reached, with relationships between them and one view, `context`.
`C4.describe` renders the model as text for the goldens `tests/golden/shop.c4.txt` and `csharp.c4.txt`
(`tests/Floorplan.Tests/C4Tests.fs`).

How external systems are named, as found on the fixtures:
- An HTTP call names the host of an absolute URL (`prices.example.org`, `api.github.com`). Without one, the
  primitive's own library names the system when it is not the platform's (`Amazon.S3`, `Azure.Storage.Blobs`);
  otherwise it is "HTTP services". Flurl, AWS's HTTP pipeline and log4net's appenders end there.
- A database or network call names the primitive's library (`StackExchange.Redis`, `MongoDB.Driver`,
  `Microsoft.EntityFrameworkCore`, `RabbitMQ.Client`); the platform's own (`System.Data`, `System.Net`) give
  "Database" and "Network". The library a call goes through (`ViaAssembly`, e.g. Dapper) is the technology.
- A namespace of the solution names no library: a Refit interface is the primitive of its calls and is declared
  in the solution, so its calls are "HTTP services". A namespace under another library's is that library
  (`Azure.Storage.Blobs.Specialized` is `Azure.Storage.Blobs`).
- Files, processes and native code are one element each. Environment and nondeterminism, and low-confidence exit
  points, are left out, as the website hides them by default.

Left over: who sends a message is not known. `EntryPoint` keeps no rule, and its evidence names only the
solution's own members, so every message consumer is called by one "Message broker". Carrying the registration
rule (or its assembly) on `EntryPoint` would name it; 13 places make entry points. A queue an application both
sends to and consumes could also give it, from the exit point's library.

### 2026-11-21: iteration 3

Containers are the application projects, and library projects no application references (tagged "library"),
inside the system. Each is tagged with what starts its code: "web application", "message consumer", "file
watcher", "background worker"; its technology is language and target framework. Relationships are now stored
from containers, and each view has its own (`C4View.Relationships`, lifted and merged): `context` shows them
from the system, `containers` from each container.

An exit point in library code belongs to the applications whose entry points are traced to it, or to every
application referencing the library when none is (code the traces do not reach). On Shop, Shop.Storage's file
write belongs to Shop.Web only.

Between applications, as found on the fixtures: a message to a queue another application consumes links them
(every consumer). An HTTP call links them only when its URL names no host, a local host or the application's own
name, and exactly one other application has the route: matching by path alone linked `CSharp.Clients`' call to
`https://status.example.org/health` to four applications with a `/health` route. A matched call is not also an
external system. Shop's worker now calls the web application's `/api/orders` on `localhost:5000` (a new exit
point in `shop.exit-points.txt`). `CSharp.Messaging` sending to `orders` links it to `CSharp.Functions`, which
consumes a queue of that name: in the fixtures the two are different brokers, which only knowing the library of
the consumer's entry point (left over from iteration 2) would tell apart.

### 2026-11-21: iteration 4

A component is a unit in a container: `component:<project>|<unit id>`, its parent the container. A library's unit
is a component of every container whose project references the library (tagged "library <project>"), since an
element has one parent; the components of Shop.Core are in both Shop.Web and Shop.Worker. Tags also say which
components handle requests and which reach out. Relationships are now stored from and to components: exit
points from their unit's component, callers to the handler's, a call between applications from the caller's
component to the handler's, and each dependency between units of a container's code is a "uses" between its
components. A view `components:<project>` per container shows its components and the outside elements they are
related to (people, external systems, other containers shown whole). `csharp.c4.txt` is now 500 lines.

