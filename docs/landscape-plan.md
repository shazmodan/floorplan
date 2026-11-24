# Analyzing a folder of solutions (a system landscape): plan

Date: 2026-11-24. Status: in progress (see **Iterations**). The owner took the four proposals in **Decisions**.
A mock of the website was generated outside the repository before the work started (see **Mock**).

## Goal

`floorplan analyze C:\Projects\` where `C:\Projects` holds a folder per system, each with its solution:

```
C:\Projects\SystemA\SystemA.sln
C:\Projects\SystemB\SystemB.sln
C:\Projects\SystemC\SystemC.sln
```

Every solution is analyzed as `floorplan analyze SystemA.sln` would analyze it, and on top of that Floorplan
derives one C4 model of the whole landscape: the systems, how they talk to each other (System A sends HTTP
requests to System B's `/orders/{id}`, System B sends messages to the queue System C consumes), the people who use
them, and the external systems they share. The website opens on that landscape. A **Solutions** menu lists the
solutions, and each one opens today's site for that solution (Overview, Projects, Namespaces, Flow, Architecture,
Entry points, Exit points, Diagnostics), with a way back.

As everywhere in Floorplan, the landscape is derived, not configured: a link between two systems is shown because
the code of one sends what the code of the other receives, and each link keeps the exit point and the entry point
it was derived from. Configuration by hand stays optional (see **Matching**, step 2).

Not goals: deployment diagrams, analyzing repositories that are not on disk, and languages Floorplan does not
read today.

## How it works today, and what it builds on

- `Discovery.resolve` turns a folder into its single solution (or single F# project). A folder with no solution
  and no project is an error today, so giving that case a meaning breaks nothing.
- `Pipeline.buildModel` turns one loaded solution into an `AnalysisModel`, with its C4 model (`C4.derive`).
  It can run once per solution unchanged. The analysis cache (summaries per assembly and closure) is already
  shared between runs, so package assemblies common to several solutions are summarized once.
- `C4.derive` already links two applications in one solution: a message to a queue another application consumes,
  and an HTTP request to a route exactly one other application has, when the URL's host is a local one or the
  application's name. Orleans grain calls go to the system that delivers them. The landscape matches the same
  kinds of evidence, across solutions instead of within one.
- `WebsiteRenderer` writes one site per model. The landscape site is a new set of pages beside it, and each
  solution keeps the site it has today.

## Design

### 1. Finding the solutions

A folder with a solution or project directly in it keeps today's meaning. A folder with neither is searched for
solutions (`*.sln`, `*.slnx`) in its subfolders:

- Folders named `bin`, `obj`, `node_modules`, `.git`, `.vs`, `artifacts`, and hidden folders are skipped.
- A folder with both `X.sln` and `X.slnx` counts once (the `.slnx`, as `resolve` prefers today).
- The search stops at a folder that has a solution: solutions in its subfolders (a `samples/` or `tools/`
  solution) belong to the same system and are not further systems. They are listed as skipped, and
  `--solutions` names them when wanted.
- New options: `--solutions <names>` and `--exclude-solutions <names>` (comma-separated, like `--include`),
  and the existing `--include`, `--exclude`, `--include-tests`, `--framework`, `--configuration`, `--no-build`
  and `--catalog` apply to every solution.

Each solution found is a **system**, named after its solution file (see **Decisions**).

### 2. Analyzing each solution

One run of `Pipeline.buildModel` per solution, one after the other (memory: a big solution's program is
disposed of before the next one is loaded). The status line shows `Solution 2 of 5: SystemB`. A solution that
fails to load or build doesn't stop the others; it is listed with its errors, and the run fails at the end as
today. Each solution's model is written as soon as it is done (its site, or its JSON), and the landscape keeps
only what it needs: projects, entry points, exit points with their payloads, and the C4 model.

Parallel runs (`--parallel 2`) are a later option, once the memory of a run is measured.

### 3. The landscape model

A new module `Floorplan.Architecture.Landscape`, beside `C4`, derives a `C4Model` from the solutions' models:

- **Systems**: one `SoftwareSystem` per solution (`system:<slug>`), with its containers inside
  (`container:<slug>/<project>`), so the landscape can be shown at two levels: systems, and containers grouped
  in their systems.
- **People and external systems**: those of every solution, merged by the key `C4.derive` already gives them
  (a URL's host, a library's name). An external system two solutions use is one element with links from both,
  so it shows that they share it.
- **Links between systems**: from matching each solution's exit points against the other solutions' entry
  points (next section). A matched exit point is a link to the system that receives it, not to an external
  system.
- **Sources**: every element and link keeps its sources as `<slug>:<id>` (an exit point, an entry point, a
  project in that solution), so the website links each one to the evidence page in that solution's site.

The new types are the landscape's own (`Landscape` = solutions + `C4Model` + diagnostics), and each solution's
`AnalysisModel` stays as it is, so the schema of today's output doesn't change.

### 4. Matching what one system sends to what another receives

In order, the first that applies decides; only exact matches of text the analysis found:

1. **Messages**: a network exit point whose payload is a queue or topic name that another system's network entry
   point receives, through the same library (a different library is a different broker), as within one solution
   today. Also gRPC services, WCF SOAP actions and Orleans grain interfaces, which are already named the same on
   both sides.
2. **A landscape file**, optional: `floorplan.landscape.json` in the analyzed folder maps hosts (and config keys)
   to systems, for hosts that name nothing (`https://10.2.3.4`, `https://api.contoso.com`):
   `{ "hosts": { "api.contoso.com": "SystemB" } }`. A call to a host it maps goes to that system's route, and
   nowhere else.
3. **HTTP with a host that names the system**: an HTTP exit point with a literal URL whose path matches one of
   another system's routes (a route parameter matches any segment: `/orders/17` and `/orders/{id}`; a segment
   only known at run time matches only a parameter), and whose host names that system or one of its
   applications (`systemb`, `system-b`, `systemb-api.internal`, compared without case, dashes and dots).
4. **HTTP to a route only one system has**: the same, when the host names nothing known, the caller's own
   system doesn't have the route, exactly one other system has it, and the route is specific (not `/`, not
   `/health`, not made only of parameters). Shown as a *probable* link (dashed, marked "route only"), because
   two deployments can run the same route.
5. **Shared data**: a database (the same server and database name in a connection string) or folder that two
   systems use is one external element both link to, so the integration through it shows. Not a link between the
   systems themselves.

Everything else stays an external system named by its host, as today. HTTP URLs whose host comes from
configuration but whose value Floorplan can't read are the common case in real systems: reading more of the
configuration (TODO 2) makes steps 3 and 4 find more.

### 5. Views

- **Landscape**: every system, the people who use them, and the external systems more than one system uses.
  External systems only one system uses are left out by default (there can be dozens), with a switch to show
  them all.
- **System context, in the landscape**: one system, the systems it talks to (both ways), its people and its
  external systems.
- **Containers across systems**: every container, grouped in a boundary per system, with the links between
  them. This is the view that answers "how does A talk to B": which application, which call, which route or
  queue. It needs the layered layout to draw several boundaries (today it draws one around the scope's parts).
- From any system, its own site's Architecture page goes on to its containers, components and code.

Layout reuses `C4.laidOut` (made internal to the assembly). The Structurizr DSL export gets a landscape
workspace: every system with its containers, and a `systemLandscape` view.

### 6. Output

```
floorplan-out/
  index.html, app.css, app.js     the landscape site
  data.js                         the landscape model (small: no solution's details)
  workspace.dsl, workspace.json   the landscape as a Structurizr workspace
  solutions/
    systema/                      SystemA's site, as today's run writes it
      index.html, data.js, ...
    systemb/
    ...
```

- One site per solution keeps each `data.js` as large as it is today, and pages load only the solution opened.
- `--format json` writes `landscape.json` and `solutions/<slug>/floorplan.json`.
- `--single-file` writes one file for the landscape, with every solution's data in it, when the total is small
  enough. Otherwise it fails and says to use the folder output (decided when measured).

### 7. Website

- The landscape site's menu: **Solutions** (a table: system, solution path, applications, projects, entry
  points, exit points, links in and out, errors), **Landscape** (the views above, with the Architecture page's
  navigation: sidebar, level switcher, Up, find, details), **Interactions** (every link between systems as a
  table: from system, application and member, to system, application and handler, what (route, queue), how,
  how it was matched, each with links to the evidence in both solutions' sites), **Diagnostics** (per solution).
- Each solution's site gets, when it is part of a landscape, a **Solutions** item first in its menu and the
  landscape's name in its title, linking back. Its Architecture page shows another system of the landscape as
  that system (linking to its site), not as an external system named by host.
- Solution sites stay usable alone (opened from their folder, or copied elsewhere).

## Iterations

Each iteration is a branch of its own, ends with every test green, and is merged on its own, as in
docs/c4-plan.md.

| # | Iteration | Branch | Status |
|---|---|---|---|
| 1 | Find the solutions in a folder, analyze each one into `solutions/<slug>/`, write a landscape index listing them | `claude/landscape-discovery` | done |
| 2 | The landscape model: systems, shared externals, links by messages and by HTTP (a naming host, the landscape file, route only); JSON and `describe` goldens | `claude/landscape-model` | done |
| 3 | Landscape views and their layout (boundaries per system), Structurizr landscape workspace | `claude/landscape-views` | done |
| 4 | The landscape site: Solutions, Landscape, Interactions, Diagnostics; back links and cross links in solution sites | `claude/landscape-website` | done |
| 5 | More matching: shared databases and folders (the landscape file and route-only links came with iteration 2) | `claude/landscape-matching` | done (folders) |
| 6 | Documentation, TODO, review on the demo projects (each solution's C4 model knowing the other systems came with iteration 4) | `claude/landscape-docs` | |

Tests: a new fixture folder `tests/fixtures/Landscape/` with three small solutions made to talk to each other:
HTTP to a host that names the system, HTTP by route only, a queue, a shared database, and one call that matches
nothing. The existing `tests/fixtures/` folder is itself a folder of six solutions, and serves as the test that
unrelated solutions give no links.

## Decisions

The owner took every proposal (2026-11-24):

1. **A solution is a system** (it is what gets built and deployed together). When a top-level folder has several
   solutions, they are several systems; the Solutions page shows each one's path, so those of one folder stand
   together.
2. **Probable links (route only) are shown, dashed**, and marked "route only".
3. **The landscape file** (`floorplan.landscape.json`) is read, and optional.
4. **Solutions inside a solution's folder** (samples, tools) are skipped unless named with `--solutions`, as
   described in **Finding the solutions**.

## Mock

`landscape-mock/index.html` at the repository root (listed in `.gitignore`, not committed) is the website as
`floorplan analyze tests\fixtures` could write it. The six fixture folders are a folder of solutions like the
one in **Goal**.

- `solutions/<slug>/`: each solution's site is today's real output, with one addition: a **◂ Solutions** link
  and a solution switcher before the menu (the switcher keeps the page you are on).
- The landscape pages (**Solutions**, **Landscape**, **Interactions**, **Diagnostics**) are drawn by a copy of
  today's `app.js` with the landscape added (marked `MOCK`). The Landscape is the Architecture page: level 1 is
  every system; opening a system shows its containers inside a boundary, with the containers of other systems
  it talks to outside it; opening a container goes to that solution's site.
- The landscape's data came from a prototype of **Matching** (steps 1 to 4) run on the six analysis models,
  laid out by `C4.laidOut` itself. The scripts are in `landscape-mock/_prototype/` (`build.py` derives the
  landscape, `layout.fsx` lays it out, `site.py` writes the site).

What the prototype found on the fixtures:

- **Route-only matching needs limits.** At first it matched gRPC's catch-all route
  (`/{unimplementedService}/{unimplementedMethod}`) and matched `/products/{id}` to `/Products/Details/{id?}`.
  A route made only of parameters now matches nothing, and a segment the caller only knows at run time (`{…}`)
  matches only a route parameter. One probable link is left: `CSharp.Clients` calls `/products`, which only
  `Routing.Conventional` has.
- **Common routes are ambiguous.** `GET /api/orders` is in four of the six solutions, so route-only matching
  rightly finds nothing for it. That is why steps 2 and 3 (the landscape file, a host that names the system)
  matter. With a sample landscape file mapping `api.example.org` to Routing, three calls from CSharp and
  Effects go to Routing.
- **Generic external systems aren't shared.** The file system, the operating system, "a database" or "HTTP
  services whose address is not known" are each system's own. Merged, they connected every system to every
  other. Only named externals (a host, a library) are merged.
- **An exit point doesn't record its HTTP method.** A call to `/orders/{id}` matches a route that takes
  DELETE, GET, PATCH and PUT. The Interactions table shows the methods the route accepts. Recording the method
  where the call names it (`GetAsync`, `DeleteAsync`) belongs in iteration 2.

## Log

- 2026-11-24: plan written, mock generated from a prototype of the matching on the six fixture solutions.
  No product code yet.
- 2026-11-24, iteration 1: `Discovery.findSolutions` and `isFolderOfSolutions`; `Pipeline.runLandscape` analyzes
  each solution in turn (a solution that fails is listed with why, and the run fails at the end) and writes
  `solutions/<slug>/` and the landscape (`Landscape` in Floorplan.Model, derived by
  `Floorplan.Architecture.Landscape`). The landscape's site is the same `app.js`, which shows the landscape's own
  pages when `window.FLOORPLAN_LANDSCAPE` is set; a solution's site in a landscape gets `FLOORPLAN_LANDSCAPE_LINK`
  and shows the way back and a solution switcher (moved here from iteration 4, as the index is no use without
  it). The Interactions page is in place, empty until iteration 2. `--single-file` with a folder is refused for
  now: one file holding every solution's data was not measured. New fixture `tests/fixtures/Landscape/` (Orders,
  Billing, Shipping, and `Billing/samples/Billing.Samples.slnx` as a nested solution). Analyzing
  `tests/fixtures` (nine solutions now) takes about 3 minutes without the cache.
- 2026-11-24, iteration 2: `Landscape.derive` builds the landscape's C4 model (systems, their containers, people
  and external systems; a host's or library's element shared, the file system, the network or "a database" each
  system's own) and matches calls between systems with steps 1 to 4 of **Matching** at once: the prototype had
  shown the four belong together (the landscape file and route-only matching only make sense beside the other
  two). The HTTP method of a call is taken from the member it calls when that names one (`GetStringAsync`,
  `PutAsync`), and a route that does not take it is no match; an interface the solution declares (Refit's) names
  none. A matched exit point's relationship to the external system goes, and an external system nothing reaches any
  more is left out. Golden `landscape.txt`: on the fixture, one link of each kind. The six older fixture solutions
  give one link, by route only (CSharp.Clients calls `/products`, which only Routing.Conventional has).
- 2026-11-24, iteration 3: the landscape's views. `context` (every system, and the people and external systems
  more than one of them uses), `context-all` (every external system too), `containers` (every container, in a
  boundary per system) and `containers:<slug>` (a system's containers, with the containers of other systems and
  the external systems they talk to); all of scope `landscape` but the last. The view of every container is laid
  out in two steps: each system becomes a box sized for its containers (rows of at most three), the boxes and the
  elements around them go through the layered layout, and the relationships are drawn straight between the
  boxes, so the boundaries never overlap. The others use `C4.laidOut`, now internal to the assembly. The website's
  Architecture page shows them when the data is a landscape: level 1 is "Landscape", level 2 a system's
  containers (or every system's), a container opens its solution's site at its components, a system's details
  link to its site, and a relationship matched by route only is dashed and labelled "probable: route only". The
  Structurizr export writes `systemLandscape` views and tags probable relationships ("Probable", dashed); the view
  of every container is left out of it (Structurizr has no view of several systems' containers). A system's own
  external systems are named with it in the landscape ("File system (Billing)").
- 2026-11-24, iteration 4: most of the website had come with iterations 1 (Solutions, Interactions, the way back)
  and 3 (Landscape). What was left were the cross links: each solution's own C4 model now knows the other systems
  of its landscape (`C4.deriveIn`, given `Landscape.knownOf`): a call another system receives goes to an element
  standing for that system (`external:system:<slug>`, tagged "in the landscape", with what the call does there),
  not to the host it is called at, and an entry point another system calls has that system among its callers.
  In a solution's site, such an element opens the other system's site. The landscape is derived from the models
  as analyzed; only the sites (and `floorplan.json` files) get the models derived again in the landscape.
- 2026-11-24, iteration 5: shared folders. A file exit point's absolute path (a template's up to its first hole)
  gives its folder, a watched folder's entry point the folder itself; a folder more than one system uses is one
  element (`external:folder:<path>`, tagged "shared folder", a Windows path compared in any case), and those uses go
  there instead of to each system's own file system. Relative paths name no folder: each process has its own
  working folder. **Shared databases are not matched yet**: the analysis does not record a connection string
  (`DbConnection.Open` has no payload, and real connection strings come from configuration), so there is nothing
  to compare; a database reached through a named client library is already one element per library. This waits
  for TODO 2 (reading more of the configuration). The fixture's Billing and Shipping now both write to
  `/srv/exchange`.
