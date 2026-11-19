# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

- **An EF Core query passed into a method and enumerated there is not seen** (`void Print(IEnumerable<Order>
  orders) { foreach ... }` called with `db.Orders`): the enumerating call's method has no query of its own, and
  each caller would need its own exit point. Queries enumerated inside a dependency are not seen either.
- **NLog and log4net configured from XML are not seen**: the targets and appenders a configuration file names are
  made by reflection, so log calls reach nothing. Only ones made in code are followed.
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
