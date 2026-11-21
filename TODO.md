# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Give Razor-generated code units**: Blazor components and Razor pages compile from `.razor`/`.cshtml` files
   into generated code, which structure analysis compiles but makes no units of, so their handlers are attributed
   to the project's global namespace unit. Next step: map a generated type to its source file through the
   generator's `#line` directives (or the `[RazorSourceChecksum]`/`[RouteAttribute]` it carries) and give the
   type the unit of the namespace it declares.
2. **Show what a configuration file's objects do when they are made**: a configured target, appender or sink is
   constructed (or its method called) as a root, so log calls reach it, but its constructor's own effects
   (opening a file, resolving a host) belong to no call site. Next step: attach them to the call that loads the
   configuration (`ReadFrom.Configuration`, `LogManager.Setup().LoadConfigurationFromAppSettings()`).
3. **See EF Core queries passed to an iterator**: the iterator copies its parameter into a second field when it
   is enumerated (`<>3__x` into `x` in `GetEnumerator`), which the summary of enumerated parameters does not
   follow; and the iterator itself runs only when it is enumerated in turn. Next step: follow the
   `<>3__x` -> `x` copy in `Queries.enumeratedParameters`, then treat enumerating the iterator as enumerating
   its parameter.
4. **Read the inputs not read yet**, each a new fixture case in an existing project:
   - Blazor's interactive events (`@onclick`, `@bind`) as inputs of the component (`CSharp.Blazor`): the
     generated `BuildRenderTree` passes the handler to `RenderTreeBuilder.AddAttribute` with an `on...` name.
   - Razor Pages route conventions (`AddPageRoute("/Index", "home")`) in `CSharp.Controllers`.
   - Azure Functions in the in-process model (a class library with `[FunctionName]`), as a new net8.0 fixture.
   - A Hangfire recurring job's schedule (`RecurringJob.AddOrUpdate(id, job, Cron.Daily())`) as the entry's address.
5. **Bring the probe-only rules into the fixtures**: the Kafka (`Produce`, `Consume`, `Subscribe`), Azure Cosmos
   DB and Elasticsearch rules are checked only on probe applications and by a catalog test that they match:
   Kafka's native library and the analysis time of the Cosmos DB and Elasticsearch clients keep them out. Next
   step: find why those clients take long to summarize (likely their serializers' reflection) and cap or skip it,
   then add them to `CSharp.Cloud`.
6. **Cover more kinds of applications**, each found by a probe first: Dapr (pub/sub subscriptions, service
   invocation, state store), Orleans grains, Minimal API route groups with filters, WCF/CoreWCF services,
   OpenTelemetry exporters as exit points, and `System.Threading.Channels` readers as internal background inputs.
7. **Summarize dependencies per project**: each project's view of the call graph keeps its virtual calls to the
   types its reference closure constructs, but dependency summaries are computed once over the whole solution
   (and cached so), so a virtual call inside a package can still reach an implementation in a package only
   another project references. Log dispatch is already limited to the project's references. Next step: find a
   case outside logging first; then key summaries by catalog and reference closure, and the cache with them.
8. **Read more of the configuration**: values read from `appsettings.json` by a constant key, sections and options
   bound to a section are read, but not a property of a nested options object (`options.Value.Rabbit.Queue`), an
   options object kept in a field (`_options = options.Value`), named options (`Configure<T>("name", section)`),
   or other configuration sources (environment variables, `AddJsonFile("other.json")`). Next step: a probe on a
   real application, to see which of these it uses.
9. **Refine the C4 model** (`docs/c4-plan.md`): name who sends a message (every consumer is called by one "Message
   broker", as `EntryPoint` keeps no rule; a WebSocket's messages count as messages too); wrap long rows of
   external systems (the C# fixtures' containers view puts 24 in one row); keep relationship labels from
   overlapping where lines meet; dependencies between types, for "uses" at the code level. Next step: carry the
   registration rule's assembly on `EntryPoint`.
10. **Export the C4 model as Structurizr DSL**: write a `workspace.dsl` next to the site (or behind an option such
    as `--c4-dsl`), so the model opens in Structurizr Lite and its PlantUML and Mermaid exporters. People map to
    `person`, the system and external systems to `softwareSystem`, applications to `container`, modules to
    `component`, each with its technology, description and tags; relationships are written at their most specific
    level (Structurizr lifts them itself), with a `systemContext`, `container` and `component` view per Floorplan
    view. The DSL has no code level: write types as components tagged "Code", or leave them out. Identifiers must
    be DSL-safe (Floorplan's ids hold `:`, `|` and `.`). Next step: a golden `workspace.dsl` for Shop, checked
    by parsing it with the Structurizr CLI if it is available.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
