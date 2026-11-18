# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **Logging noise.** On a real C# solution every Serilog call reports the file sink's file reads and writes,
  the console and the clock (`docs/phase-2-review.md`), over half of what the site shows. Proposal: report effects
  reached through a logging library's logger (Serilog's `ILogger`, `Microsoft.Extensions.Logging.ILogger`) once
  per sink, as configuration of the application, instead of at every log call.
- **EF Core writes are not seen.** `DbContext.SaveChanges*` reaches no primitive (EF runs commands through its
  own service container). A catalog rule for it is one case of the catalog-rule fallback below.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
