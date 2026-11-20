# C# support: status

Date: 2026-11-17. Plan section 10, Phase 7. Fixtures: `tests/fixtures/Mixed` (F# and C# projects calling each
other), `tests/fixtures/CSharp` (routing in C#) and `Shop.Storage` in `tests/fixtures/Shop`.

## Done: structure analysis

`Floorplan.Structure.CSharp` is the Roslyn-based `IStructureAnalyzer` (plan section 3.5). C# projects now have full
support instead of appearing as one limited-support node.

- **Units are namespaces** (open decision 1 in the plan, decided 2026-11-17). Each namespace that declares a
  type, delegate or top-level statement in the project's own source is a unit of kind `namespace`. Types declared
  outside any namespace, and top-level statements, form the project's global namespace unit (empty name, shown as
  "(global namespace)"). A namespace that only contains other namespaces is not a unit. Generated files under
  `obj/` (global usings, assembly info) are compiled but give no units and no edges.
- **Edges**: every name in the source that Roslyn binds to a type or member (including attributes and target-typed
  `new()`) is an edge from the namespace the code is in to the declaring type's unit. `using` directives give no
  edges: they name namespaces and may be unused.
- **Across languages**: an analyzer names an edge's target by what declares the symbol (a C# type, an F# module).
  Structure analysis resolves that name to the innermost unit that contains it, falling back to the target project's
  global namespace. So C# code that calls an F# module gets an edge to that module, and F# code that uses a C# type
  gets an edge to its namespace.
- **Cycles** between namespaces are common in C#. The layout already collapses them into one node (`Billing ⇄
  Storage` in the Mixed fixture).
- **Effects** in C# code are attributed by PDB sequence points and, where a namespace's code spans several files,
  by the declaring type's name. Types in no namespace belong to the global namespace unit.
- The compilation is built from the MSBuild evaluation (compile items, resolved references, `DefineConstants`),
  not from a workspace, and is cached per project like the F# results. Compile errors are reported as warnings
  (`structure.compile-error`) and leave the result uncached.

The architecture test now also keeps Roslyn and `*.CSharp` projects out of `Floorplan.Analysis`,
`Floorplan.Layout` and `Floorplan.Rendering`.

## Done: entry points and C# compiler output

Fixture: `tests/fixtures/CSharp`, the routing styles of the Routing fixture written in C#, built for net8.0 and
net10.0 with the same golden results (`tests/golden/csharp.entry-points.txt`):

| Project | Routing | Result |
|---|---|---|
| `CSharp.Endpoints` | Minimal APIs: method groups, lambdas, async lambdas, a group handed to a method, a nested group behind `RequireAuthorization`, a template built at run time | Every constant route complete, one entry each; the run-time template partial |
| `CSharp.Controllers` | `[ApiController]` attribute routes and `MapDefaultControllerRoute` | As for F# controllers |
| `CSharp.Middleware` | `==`, `StartsWith`, `HttpMethods.IsOptions`, `Split` with indexing, a `switch` statement on the method | As for the F# middleware |
| `CSharp.Switch` | A switch expression on a `(method, path)` tuple with seven string cases (Roslyn compares hashes, then strings), and list patterns over segments (`["api", "orders", var id]`) | Every case complete, with a path parameter for `var id` |
| `CSharp.Table` | A `Dictionary<string, Func<string>>` initializer behind `HttpListener` | As for the F# table |

Most of what C# emits needed no new code: it is either shared by every .NET compiler (delegates through `ldftn`,
async state machines, iterators) or made of platform calls the analysis reads anyway (`String.op_Equality` in
string switches, array length and element access in list patterns, `string.Format`).
`Floorplan.IL.Codegen.CSharp` holds what is Roslyn's own:

- **Delegate caches.** Roslyn caches the delegate for a lambda (`<>c.<>9__0_1`) or a static method group
  (`<>O.<0>__Name`) in a static field and creates it only when the field is null. The symbolic run read such a
  field as unknown, so every minimal API route had a second entry whose handler was "a library function". Those
  fields now start out null (`ICodegenRecognizer.IsLazyCache`).

Also new, outside the recognizer:

- **Interpolated strings** built with the platform's `DefaultInterpolatedStringHandler` are read as payload
  templates (`$"orders/{id}.json"` gives `orders/{…}.json`), as `string.Format` already was.
- **Readable C# names on the website**: a lambda is shown by its enclosing method (`Admin › lambda in Map`,
  `Program › lambda in top-level statements`), an async body as `Store › async body of Load`, a local function by
  its name. The compiler's name stays in the tooltip.
- The global namespace unit is shown by its label wherever the site names a unit.

## Done: effects, dependencies, source generators

- **`CSharp.Effects`** ports the F# Effects fixture (`tests/golden/csharp.exit-points.txt`): every category
  called directly, ADO.NET through SQLite and Npgsql (using declarations, an object initializer), Dapper and
  Flurl as wrapper libraries, a lambda capturing a local, an interface, an implementation only named in a
  dependency-injection registration, an async method, an iterator, literal/constant/template/dynamic payloads,
  `LibraryImport`, and reflection as a blind spot. All give the F# results.
- **C# `dynamic`** is a blind spot (`blind-spot.dynamic`), except where MVC defines what happens: a member of
  `ViewBag` read or written (an entry of the view data), in a controller, view or page, and a Razor view writing
  a dynamic value to the page (`@ViewBag.Title`). Fixture: `CSharp.Controllers/Pages.cs` and its view (GitHub
  issue 2). After a run the console prints three warnings of each kind and how many more there are; the site
  and the JSON model list them all.
- **Log calls** (F# and C#): a call matching a catalog rule of kind `logger` (Serilog's `ILogger` and `Log`) reaches
  whatever sinks the logger is configured with, the same at every call. What a unit's log calls reach is one exit
  point per effect, shown at its first log call, with all of them in the evidence; traces follow every one of them.
  Configuring the logger is the application's own call and is reported as before. A library's log of its own
  workings, written only when the application turns it on, is not followed in the library's code: catalog rules
  of kind `self-log` name Serilog's `SelfLog.WriteLine`, NLog's `InternalLogger` log methods and log4net's
  `LogLog.Debug`. So making a logger (Serilog's `ForContext`) is no exit point, and a log call's clock is the log
  event's timestamp. Turning such a log on is followed (NLog's `internalLogFile` setting); log4net's warnings and
  errors go to the console by default, and are followed too. Fixtures: `CSharp.Effects/Logging.cs`,
  `CSharp.Endpoints/Audit.cs`.
- **Log calls through `Microsoft.Extensions.Logging`** are grouped the same way. The library hands each call on to
  the loggers its providers make, and its own code is not followed: in ASP.NET Core it is part of the shared
  framework, and elsewhere the container builds its loggers. The `logger.microsoft` rules name the member those
  loggers implement (`"dispatch": "Microsoft.Extensions.Logging.ILogger::Log"`), and a log call reaches what each
  constructed implementation of it reaches. Two `callback` rules make the implementations reachable: constructing
  a provider outside the framework links `ILoggerProvider.CreateLogger` to its implementation, which constructs the
  provider's logger, and that links `ILogger.Log`. The call graph links every callback rule this way; the summary
  cache keeps what a callback reaches in each assembly, because a later run does not see the cached code that
  constructs the logger. Only Serilog's provider is tested: fixtures `CSharp.Effects/MicrosoftLogging.cs` (a
  console app with a container) and `CSharp.Endpoints/Shipping.cs` (ASP.NET Core). `LoggerMessage.Define` makes a
  delegate that calls `ILogger.Log`: invoking it is the log call (`"logCall": "delegate"`), found where the
  delegate comes from the `Define` call directly or from a field its type stores it in. A call to a
  `[LoggerMessage]` method is a log call too (`"logCall": "attribute"`), and the generated method's own log
  call is not, so each counts once. Fixture: `CSharp.Effects/LoggerMessages.cs`, and `Parcels.Hold` in
  `CSharp.Endpoints/Shipping.cs`. A dependency's own log calls through `Microsoft.Extensions.Logging`
  (Entity Framework Core logs every save) are not followed for effects, as where the library is part of the
  shared framework: what the sinks do belongs to the logging configuration, and shows at the application's log
  calls.
- **NLog and log4net** log calls are grouped the same way (`logger.nlog*`, `logger.log4net`). NLog's `Logger` is a
  class whose code is followed to the targets it is configured with. log4net makes its loggers by reflection and
  through a delegate, which are not followed, so its rule dispatches to the appenders instead
  (`"dispatch": "log4net.Appender.IAppender::DoAppend"`, with a callback rule): a log call reaches what each
  constructed appender's `DoAppend` reaches. Targets and appenders named in a configuration file are made by
  reflection; catalog rules of a new kind, `configured`, say where a configuration names them
  (`configured.nlog-target`, `configured.log4net-appender`), and each type named is analyzed as constructed, as
  if in code. The files read are the `*.config` files in the project's output folder, where the library finds
  them at run time (`nlog.config`, `log4net.config`, an `<nlog>` or `<log4net>` section of `App.dll.config`), and
  `*.xml` files named after the configuration; and the `appsettings*.json` files there, for the `"NLog"` section
  NLog.Extensions.Logging reads (targets under `"targets"`, wrapped ones under `"target"`, keys in any case).
  NLog's `xsi:type` or `"type"` names a target by the alias its `[Target]` attribute gives it, log4net's `type` by
  its type name. Each type is an info diagnostic (`configured.type`), and a name that is no type the analysis can
  read a warning (`configured.unresolved`). Serilog's sinks and enrichers in the `"Serilog"` section of
  `appsettings.json` (Serilog.Settings.Configuration) are named by the methods that add them (`"WriteTo": [{
  "Name": "File" }]` calls `WriteTo.File`): the rules `configured.serilog-sink` and `configured.serilog-enricher`
  say `"names": "methods"`, and every public method of that name on `LoggerSinkConfiguration` (or extension
  method on it) in Serilog or an assembly that references it is analyzed as called. Configuration loaded from
  elsewhere at run time is not read. Getting a logger (`LogManager.GetCurrentClassLogger()`,
  `LogManager.GetLogger`) loads the configuration, and stays an exit point of its own. Fixture:
  `CSharp.Effects/OtherLoggers.cs`, with `nlog.config`, `log4net.config` and `appsettings.json`.
- **Entity Framework Core** runs its commands through its own service container, which the call graph does not
  follow, so the catalog names its calls to the database directly (`database.ef-*`, the first sink rules on a
  library rather than the platform): `SaveChanges`, `Find`, `ExecuteSql*` (with its SQL as the payload; an
  interpolated one is a template, read from the `FormattableStringFactory.Create` call C# makes for it),
  `ExecuteDelete`/`ExecuteUpdate`, and the async calls that run a query (`ToListAsync`, `FirstOrDefaultAsync`,
  ...). A query run by enumerating it is found through catalog rules of a new kind, `query`: a `DbSet<T>` is a
  query, and so are the results of `FromSql*` and `SqlQuery*`, with their SQL as the payload. A call that
  enumerates a query runs it: `foreach` and `await foreach` (`GetEnumerator`, `GetAsyncEnumerator`), a LINQ call
  that returns something other than a sequence (`ToList()`, `First()`, `Count()`), or a collection made from it.
  The query is followed back through the calls that build on it unrun (`Where`, `OrderBy`, `Include`,
  `AsNoTracking`) to a `DbSet` value (a property, a field, a parameter, `Set<T>()`) or a `FromSql*` call, and
  into the methods called on the way that return a sequence (a repository's `IQueryable<T> All() => db.Orders`,
  directly or through an interface, an extension method that adds a `Where`), up to four calls deep, with their
  parameters standing for what each call passes. A call that runs a query and has no payload of its own
  (`ToListAsync`) shows the SQL of the `FromSql*` it was made from, worked out at the calls that returned it.
  A query passed to a method that enumerates the parameter (an `IEnumerable<T>` or `IQueryable<T>`), itself or
  through the methods it passes it on to, or through a compiler-generated type it is stored in whose methods
  enumerate the field (an async method's state machine, a lambda's closure), runs at the call that passes it,
  which is the exit point: in user code or in a dependency, not in an iterator. Framework methods that enumerate
  an argument count as well: `String.Join`, `String.Concat`, and the `System.Collections.Generic` methods that
  take a sequence (`AddRange`, `UnionWith`). Fixture: `CSharp.Effects/EntityFramework.cs`
  (`CSharp.Effects.Reporting` for queries across methods, `CSharp.Effects.Printing` for queries passed in).
- **SignalR hubs**: `MapHub<THub>(pattern)` is a registration (`http-in.map-hub`) whose `"hub-type"` payload is
  the index of the type argument that is the hub. SignalR is part of the shared framework, which is not followed,
  and invokes hub methods by name through reflection, so they are read from the hub's declaration, as MVC actions
  are: its public instance methods and those of its base classes outside the framework, except constructors,
  accessors, generic methods and overrides of the framework's virtual methods (`OnConnectedAsync`,
  `OnDisconnectedAsync`, `Dispose`). Each is an entry point at the pattern (with a route group's prefix), partial,
  as the invocation's target, not the HTTP method, chooses the handler; `[HubMethodName]` gives the name clients
  invoke it by (`http-in.signalr-hub-method-name`), shown in the evidence. Fixture: `CSharp.Endpoints/Chat.cs`.
- **gRPC services** (Grpc.AspNetCore): `MapGrpcService<TService>()` is a registration
  (`http-in.map-grpc-service`) whose `"grpc-service-type"` payload is the index of the type argument that is the
  service. The library binds a service by reflection, so the binding is read the way it does it, from code in the
  application's own assembly that Grpc.Tools generates: `[BindServiceMethod(typeof(Stock), "BindService")]` on the
  service's generated base class names the type and the static method that binds it (`http-in.grpc-bind-service-method`,
  read from the attribute's `typeof` argument); each overload of that method calls `AddMethod(descriptor, handler)`
  (`http-in.grpc-add-method`) with a descriptor its type's static constructor makes, `new Method<TRequest,
  TResponse>(type, serviceName, methodName, ...)` (`http-in.grpc-method`), and a delegate to the base class's
  method. Each method the service overrides is an entry point at `POST /{serviceName}/{methodName}`, complete;
  one it leaves to the generated base class (which answers that it is unimplemented) is not. Grpc.AspNetCore's own
  catch-all for unknown services and methods stays an entry point of the library. Fixture: `CSharp.Grpc`.
- **Hosted services**: background work the generic host starts by itself is an entry point of category
  `background`. `AddHostedService<T>()` is a registration (`background.add-hosted-service`) whose
  `"hosted-service-type"` payload is the index of the type argument that is the service, and callback rules of
  category `background` name what the host calls: `IHostedService.StartAsync` and `StopAsync`
  (`background.hosted-service`), a `BackgroundService`'s `ExecuteAsync` (`background.background-service`). Each
  such member the application implements is an entry point, with no route; its trace is what the work does. A
  hosted service the application never registers is not one. Fixture: `CSharp.Worker` (Microsoft.Extensions.Hosting
  as a package, on the Worker SDK).
- **`CSharp.Delegated`** with `Dependency/CSharp.Library`: the middleware router in a library referenced as a
  built assembly (a dependency, like a package), with the application's handlers passed as method groups in a
  record. Same routes as the F# `Routing.Delegated`.
- **Source generators** run during structure analysis: the generator assemblies the compiler is given
  (`/analyzer:`) are loaded, one load context per folder (projects on different .NET versions have generators of
  the same name), and their output is compiled so that code calling it binds. Without this, every `LibraryImport`,
  `[GeneratedRegex]`, JSON or logging generator gave a compile error.
- **Types of a namespace**: a namespace unit lists the types it declares (`CodeUnit.Types`, shown on its page).
- **SQL text** (F# and C#): an ADO.NET command's `CommandText` is the payload of its `Execute*` calls, whether
  set as a property, in an object initializer or through the constructor; built from values it is a template
  (`DELETE FROM {…}`), and a command made elsewhere stays dynamic. A request's URL is read the same way
  (`RequestUri`, or the URL given to `new HttpRequestMessage(method, url)`).
- **Payloads through libraries** (F# and C#, `docs/library-payloads-plan.md`): for an effect behind a dependency,
  the payload is worked out from the dependency's own code, in terms of its arguments, and evaluated at the call
  site; no rule names the library. Arguments passed on, text built from them, properties and fields of objects
  (set by setters, constructors, or methods that return the object), structs passed by reference, async methods,
  iterators and F# `task { }` / `seq { }` are followed. Dapper's `Query` and `Execute` show their SQL, and so do its async
  methods (`QueryAsync`, `QuerySingleAsync`, `ExecuteAsync`, `ExecuteScalarAsync`). A value returned by a
  method that builds it from its arguments (a helper that appends paging to the SQL it is given) is followed
  too; on a real solution every one of 109 Dapper calls shows its SQL (`docs/phase-2-review.md`). When a
  library reaches the primitive by several routes, the shortest one whose payload is known is shown, with its
  evidence and the primitive that route ends at. Values kept in collections or caches, or rebuilt from parsed objects (Flurl's `Url`), stay dynamic.
  Fixtures: `Dependency/CSharp.Wrappers` and `Effects/Dependency/Effects.Wrappers`, both built optimized as
  packages are.
- **Route parameter names** (F# and C#): a segment taken as a parameter is named after the handler parameter
  that receives it (`/orders/{id}` for `Show(HttpContext ctx, string id)`), for control-flow routes as it already
  was for platform routes.

## Not done yet

- Generators that read additional files or MSBuild properties through analyzer config options get none, so code
  depending on what those generate may not bind; it is then reported as a compile error and the result is not
  cached.
