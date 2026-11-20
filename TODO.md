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

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
