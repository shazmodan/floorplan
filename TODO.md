# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **EF Core queries run by enumerating them are not seen** (`foreach`, `ToList()` over a `DbSet`): they are
  LINQ calls, the same as over a list. `FromSql*`/`SqlQuery*` SQL is not shown either (the query runs later), and
  an interpolated `ExecuteSql` is dynamic (`FormattableStringFactory.Create` is not read as a template).
- **`[LoggerMessage]` and `LoggerMessage.Define` log calls are not grouped.** They call `ILogger.Log` through a
  delegate the framework makes, so no `logger` rule matches them. NLog and log4net have no rules either.
- **Serilog's `ForContext` reaches the clock.** On mmddd, 9 `ForContext` calls (which create a logger, not log) are
  still one nondeterminism exit point each. Hidden by default; low priority.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
