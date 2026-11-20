# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **An EF Core query passed to an async method or iterator that enumerates it is not seen**: the parameter is kept
  in the state machine's field, which the summary of enumerated parameters does not follow. Nor are queries
  enumerated in a lambda, or by a framework method (`string.Join`, `List<T>.AddRange`).
- **NLog configured in appsettings.json is not read** (NLog.Extensions.Logging's "NLog" section): only XML
  configuration files in the output folder are.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
