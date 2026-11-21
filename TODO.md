# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **An EF Core query passed to an iterator that enumerates it is not seen**: the iterator copies its parameter
  into a second field when it is enumerated (`<>3__x` into `x` in `GetEnumerator`), which the summary of
  enumerated parameters does not follow; and the iterator itself runs only when it is enumerated in turn.
- **What a configuration file's objects do when they are made is not shown**: a configured target, appender or
  sink is constructed (or its method called) as a root, so log calls reach it, but its constructor's own effects
  (opening a file, resolving a host) belong to no call site. The call that loads the configuration
  (`ReadFrom.Configuration`, `LogManager.Setup().LoadConfigurationFromAppSettings()`) could carry them.

- **One call graph serves every project of a solution**: types any project instantiates are instantiated for all
  (rapid type analysis), so a project's virtual calls can reach implementations only another project constructs.
  Log dispatch keeps to the project's referenced assemblies, and a shared framework's copy of an assembly wins over
  a package's, but other virtual calls and the symbolic routing still see the whole solution.
- **Razor-generated code gives no units**: Blazor components and Razor pages compile from `.razor`/`.cshtml` files
  into generated code, which structure analysis compiles but makes no units of, so their handlers are attributed
  to the project's global namespace unit.
- **Inputs read from the program's configuration are not resolved**: a queue, topic or route named in
  `appsettings.json` or an options class (`options.Value.QueueName`) is unresolved, as is a Redis channel made with
  `RedisChannel.Literal("...")`.
- **Not read yet**: Blazor's interactive events (`@onclick`) as inputs; Razor Pages route conventions
  (`AddPageRoute`); Azure Functions in the in-process model (a class library, not an application); a Hangfire
  recurring job's schedule; NServiceBus handlers (found by assembly scanning, with no registration).
- **Checked only outside the fixtures**: the Kafka (`Produce`, `Consume`, `Subscribe`) and Azure Cosmos DB rules, on
  probe applications: Kafka's native library and the Cosmos DB client's analysis time keep them out of the
  fixtures.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
