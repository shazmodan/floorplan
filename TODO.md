# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Routes of the owner's real F# application** (waiting for more information from the owner): its routes,
   matched with active patterns (`GET`, `POST`, ... made by partially applying inline matchers over a request
   record), are still not shown correctly. The same routing library is already read correctly in a stand-in: all
   routes, with a handler per case, and with `PUT`, `PATCH`, `DELETE`, `POSTQ`, `HEAD` and the total `Request`
   pattern (fixture `Routing.Wrapped`). So the cause is in the application's own code. Needed from the owner,
   any of:
   - confirmation that it was analyzed with a build from 0e2d89b or later;
   - what is wrong (routes missing, `/X/*`, parameters missing, wrong handler or method), with two or three of
     the bad routes and the match cases they come from;
   - the shapes that differ from the stand-in: `when` guards, nested matches, handlers taking the whole request
     or returning `Async`/`Task`, routes split across modules, one very large match, the startup and middleware
     wiring;
   - or a copy of the application on this machine, kept untracked like the demo projects.
2. **Read more of the configuration**: values read from `appsettings.json` by a constant key, sections and options
   bound to a section are read, but not a property of a nested options object (`options.Value.Rabbit.Queue`), an
   options object kept in a field (`_options = options.Value`), named options (`Configure<T>("name", section)`),
   or other configuration sources (environment variables, `AddJsonFile("other.json")`). Next step: a probe on a
   real application, to see which of these it uses.
3. **Folder of solutions, what is left** (the feature is done, docs/landscape-plan.md): shared databases, once
   connection strings are read (after item 2); `--single-file` for a folder of solutions (refused for now);
   analyzing solutions in parallel, once a run's memory is measured; a run over both demo projects together, once
   the API demo can be restored (its private package feed is not reachable from this machine, so only the admin
   demo was analyzed, and no link between them was found).

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- C# lambdas inside a library (a display class made in another method) give an unknown payload.
- Values kept in collections or caches before use stay dynamic.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
