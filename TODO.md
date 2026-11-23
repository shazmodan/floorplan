# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Resolve assemblies per project**: one assembly set serves the solution, and a shared framework's copy of an
   assembly wins over a package's, so which copy a project reads depends on the other projects. A package copy of
   `Microsoft.Extensions.Hosting` (or Configuration, FileProviders, Caching.Memory), which the project's build
   resolved because it is newer, is read as the framework's and not followed when another project's search paths
   hold the framework: analyzed alone, `CSharp.Worker`, `CSharp.Functions`, `CSharp.Telemetry`, `CSharp.WcfClient`
   and `CSharp.Effects` show environment and file effects through it that the solution run does not. Next step:
   decide which copy is right for a project (the one its build resolved, most likely) and whether one call graph
   can still serve the solution.
2. **Read more of the configuration**: values read from `appsettings.json` by a constant key, sections and options
   bound to a section are read, but not a property of a nested options object (`options.Value.Rabbit.Queue`), an
   options object kept in a field (`_options = options.Value`), named options (`Configure<T>("name", section)`),
   or other configuration sources (environment variables, `AddJsonFile("other.json")`). Next step: a probe on a
   real application, to see which of these it uses.

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
