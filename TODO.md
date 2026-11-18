# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **Review payloads on a real solution.** Run on kgrzybek/modular-monolith-with-ddd (.NET 8, Dapper throughout;
  cloned to `C:\utveckling\real-projects\mmddd`, built with `NuGetAudit=false` because the project treats the
  IdentityServer4 audit warning as an error). Check which SQL texts and URLs come through, which stay dynamic and
  why, and record the findings in a doc like `docs/phase-2-review.md`.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
