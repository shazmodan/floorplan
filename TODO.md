# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

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
