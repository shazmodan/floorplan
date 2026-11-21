# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Give each project its own call graph**: one call graph serves every project of a solution, so types any
   project instantiates are instantiated for all (rapid type analysis), and a project's virtual calls can reach
   implementations only another project constructs. Log dispatch keeps to the project's referenced assemblies, and
   a shared framework's copy of an assembly wins over a package's, but other virtual calls and the symbolic routing
   still see the whole solution. Next step: build the instantiated-type set per project from its reference closure
   (as `DispatchFor` does), keeping the summaries shared. Expect golden changes in every fixture project.
2. **Resolve names read from configuration**: a queue, topic or route named in `appsettings.json` or an options
   class (`options.Value.QueueName`) is unresolved. Next step: `Configured.fs` already reads `appsettings*.json`;
   evaluate `IConfiguration["Section:Key"]` and `GetSection(...).Bind`/`Configure<TOptions>(section)` to the value
   there. Also: a Redis channel made with `RedisChannel.Literal("...")` shows as `unresolved` (the Redis
   subscription in `CSharp.Clients`); `RedisChannel.Literal` should be transparent, as `op_Implicit(string)` is.
3. **Give Razor-generated code units**: Blazor components and Razor pages compile from `.razor`/`.cshtml` files
   into generated code, which structure analysis compiles but makes no units of, so their handlers are attributed
   to the project's global namespace unit. Next step: map a generated type to its source file through the
   generator's `#line` directives (or the `[RazorSourceChecksum]`/`[RouteAttribute]` it carries) and give the
   type the unit of the namespace it declares.
4. **Show what a configuration file's objects do when they are made**: a configured target, appender or sink is
   constructed (or its method called) as a root, so log calls reach it, but its constructor's own effects
   (opening a file, resolving a host) belong to no call site. Next step: attach them to the call that loads the
   configuration (`ReadFrom.Configuration`, `LogManager.Setup().LoadConfigurationFromAppSettings()`).
5. **See EF Core queries passed to an iterator**: the iterator copies its parameter into a second field when it
   is enumerated (`<>3__x` into `x` in `GetEnumerator`), which the summary of enumerated parameters does not
   follow; and the iterator itself runs only when it is enumerated in turn. Next step: follow the
   `<>3__x` -> `x` copy in `Queries.enumeratedParameters`, then treat enumerating the iterator as enumerating
   its parameter.
6. **Read the inputs not read yet**, each a new fixture case in an existing project:
   - Blazor's interactive events (`@onclick`, `@bind`) as inputs of the component (`CSharp.Blazor`): the
     generated `BuildRenderTree` passes the handler to `RenderTreeBuilder.AddAttribute` with an `on...` name.
   - Razor Pages route conventions (`AddPageRoute("/Index", "home")`) in `CSharp.Controllers`.
   - Azure Functions in the in-process model (a class library with `[FunctionName]`), as a new net8.0 fixture.
   - A Hangfire recurring job's schedule (`RecurringJob.AddOrUpdate(id, job, Cron.Daily())`) as the entry's address.
7. **Bring the probe-only rules into the fixtures**: the Kafka (`Produce`, `Consume`, `Subscribe`), Azure Cosmos
   DB and Elasticsearch rules are checked only on probe applications and by a catalog test that they match:
   Kafka's native library and the analysis time of the Cosmos DB and Elasticsearch clients keep them out. Next
   step: find why those clients take long to summarize (likely their serializers' reflection) and cap or skip it,
   then add them to `CSharp.Cloud`.
8. **Cover more kinds of applications**, each found by a probe first: Dapr (pub/sub subscriptions, service
   invocation, state store), Orleans grains, Minimal API route groups with filters, WCF/CoreWCF services,
   OpenTelemetry exporters as exit points, and `System.Threading.Channels` readers as internal background inputs.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
