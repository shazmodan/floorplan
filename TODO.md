# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Minimal API route groups with filters** (put first by the owner): `MapGroup` prefixes and endpoint filters
   (`AddEndpointFilter`, on a group or an endpoint), and what a filter runs before the handler. Next step: a probe.
2. **Cover more kinds of applications**, each found by a probe first: Dapr (pub/sub subscriptions, service
   invocation, state store), Orleans grains, WCF/CoreWCF services, OpenTelemetry exporters as exit points, and
   `System.Threading.Channels` readers as internal background inputs.
3. **Summarize dependencies per project**: each project's view of the call graph keeps its virtual calls to the
   types its reference closure constructs, but dependency summaries are computed once over the whole solution
   (and cached so), so a virtual call inside a package can still reach an implementation in a package only
   another project references. Log dispatch is already limited to the project's references. Next step: find a
   case outside logging first; then key summaries by catalog and reference closure, and the cache with them.
4. **Read more of the configuration**: values read from `appsettings.json` by a constant key, sections and options
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
