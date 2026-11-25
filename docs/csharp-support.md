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
- **Razor-generated code**: code a source generator makes from a file of the project that is not C# (a `.razor`
  component, a `.cshtml` page or view), found by its `#line` directives mapping back to that file, declares units
  as the project's own source does: a Blazor component is a type of the namespace its folder gives
  (`CSharp.Blazor.Components.Pages`). A namespace only generated code declares is located in the first file it is
  made from. What the generated types' code uses gives edges; the assembly attributes that list them give none.
- **Razor pages and views** compile to types named after their path (`Areas_Admin_Pages_Audit`) in a namespace of
  the generator's own (`AspNetCoreGeneratedDocument`), and each records its file as its `Identifier` metadata
  (`[RazorCompiledItemMetadata("Identifier", "/Areas/Admin/Pages/Audit.cshtml")]`). A unit of kind `folder` stands
  for each folder (`Areas/Admin/Pages`), and lists its pages by file (`Audit.cshtml`); the unit's aliases join each
  file to its compiled type, which members, entry points and type uses name. The C4 code level shows a page by its
  file. Every generated tree is read for the metadata: a page with no code in it maps no line back to its file.
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
| `CSharp.Endpoints` | Minimal APIs: method groups, lambdas, async lambdas, a group handed to a method, a nested group behind `RequireAuthorization`, a template built at run time, route groups with endpoint filters (`CSharp.Endpoints.Filtered`) | Every constant route complete, one entry each; the run-time template partial; a filter's effects in the trace of each endpoint it filters |
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

- **Route groups with endpoint filters.** A convention chained on a group before its routes
  (`app.MapGroup("/v1").AddEndpointFilter(...)`, `.WithTags(...)`, `.RequireAuthorization()`) lost the group's
  prefix: `/v1/items` was reported as `/items`, complete. A generic method whose result is its first parameter's
  type argument (`TBuilder M<TBuilder>(this TBuilder builder, ...)`) can only return the builder it is given, so
  the prefix is followed through it, in the symbolic run and in the static scan (`PlatformRouting.returnsItsBuilder`).
  Endpoint filters are registrations (`http-in.endpoint-filter`: `AddEndpointFilter`, `AddEndpointFilterFactory`;
  `"builder"` is the argument the call returns, `"endpoint-filter"` the filter, or without that argument the call's
  last type argument, `AddEndpointFilter<TFilter>()`). The symbolic run records each filter on the builder it is
  added to; an endpoint's builder is now an object made on its group, so an endpoint has its own filters and those
  of its group and the groups around it. What a filter runs (a lambda, a factory, a filter type's `InvokeAsync`) is
  listed in the entry point's `before`, its trace includes their effects, and the evidence names each filter. A
  filter's own code is no entry point of its own: a lambda filter that reads the request path had been one.

- **Interpolated strings** built with the platform's `DefaultInterpolatedStringHandler` are read as payload
  templates (`$"orders/{id}.json"` gives `orders/{…}.json`), as `string.Format` already was. Since 2026-11-22
  `string.Format` puts the values of its arguments into plain `{n}` items (an item with an alignment or a format
  string stays a hole), and an integer constant is its digits where a string is made of it: boxed for a format,
  or passed for a parameter the callee formats. An integer argument on its own (an enum, a count) is no text.
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
  method on it) in Serilog or an assembly that references it is analyzed as called. What each object does when
  it is made (its constructor, or the method called: `WriteTo.File` creates the log's folder) belongs to the call
  that loads the file, the rule's `loadedBy` members, and is reported there: `ReadFrom.Configuration` for
  Serilog's section, `LoadConfigurationFromSection`, `LoadConfigurationFromAppSettings`, `AddNLog` or `UseNLog`
  for NLog's, the first `GetCurrentClassLogger`, `GetLogger` or read of `LogManager.Configuration` for
  nlog.config, and `XmlConfigurator.Configure` or `ConfigureAndWatch` for log4net's (an
  `[assembly: XmlConfigurator]` attribute is not read). Its arguments come from the file, so a payload keeps
  only what the library's code fixes. Fixture: `CSharp.Effects/LoadedLoggers.cs`. Configuration loaded from
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
  which is the exit point: in user code or in a dependency, not in an iterator. A query passed to an iterator
  does not run at that call: the iterator keeps it (`<>3__orders`, copied into `orders` when it is enumerated)
  and runs it where what the iterator returns is enumerated (`Titles.Of("#", db.Orders).ToList()`), or at the
  call to a method that enumerates it so (`int Count(IEnumerable<Order> orders) => Of("#", orders).Count()`),
  whichever argument the iterator is passed it as. Framework methods that enumerate
  an argument count as well: `String.Join`, `String.Concat`, and the `System.Collections.Generic` methods that
  take a sequence (`AddRange`, `UnionWith`). Fixture: `CSharp.Effects/EntityFramework.cs`
  (`CSharp.Effects.Reporting` for queries across methods, `CSharp.Effects.Printing` for queries passed in).
- **Razor Pages**: `MapRazorPages()` (`http-in.map-razor-pages`, role `"razor-pages"`) maps the pages compiled
  into the application. Each page is a class carrying `[RazorCompiledItemMetadata(key, value)]`; the rule
  `http-in.razor-page-metadata` names the keys whose values are the page's file (`"Identifier"`,
  `/Pages/Shop/Item.cshtml`) and its `@page` template (`"RouteTemplate"`). The route is the file's path under
  `/Pages` (`/Areas/{area}/Pages` for an area, `/{area}/...`), an `Index` page at its folder too, followed by the
  template unless it starts with `/`. The page's model (the type of its `Model` property) handles requests with
  its `On{Verb}[Name][Async]` methods: one complete entry point per method, at its HTTP method; a named handler
  (`OnPostRemove`) has the query condition `handler=Remove`. A page whose model has none answers GET itself (its
  generated `ExecuteAsync`). A route convention with constant arguments gives the page it names one more route:
  `Conventions.AddPageRoute("/Shop/Index", "home")` (`http-in.razor-page-route`) and
  `AddAreaPageRoute("Admin", "/Audit", "audit/{year:int?}")` (`http-in.razor-area-page-route`), whose route, from the
  application's root, replaces the page's own template. Other conventions (`AddFolderRouteModelConvention`, a
  page route model convention class) are not read. Fixture: `CSharp.Controllers` (`Pages/`, `Areas/Admin/Pages/`,
  the conventions in `Program.cs`).
- **Blazor (Razor components)**: `MapRazorComponents<TRootComponent>()` (`http-in.map-razor-components`, whose
  `"components"` payload is the index of the type argument) maps the routed components of the root component's
  assembly, and `MapBlazorHub()` (`http-in.map-blazor-hub`) those of the application. A routed component carries a
  `[Route(template)]` for each `@page` directive (`http-in.component-route`): each is a complete GET entry point.
  Its handler is the first lifecycle method the component defines (`OnInitializedAsync`, `OnInitialized`,
  `OnParametersSetAsync`, `OnParametersSet`), or else its `BuildRenderTree`. With interactive server rendering on
  (`AddInteractiveServerRenderMode()`, `http-in.blazor-interactive-server`, or `MapBlazorHub()`: a `"component-events"`
  payload), the browser sends a component's events over the circuit's hub, and each runs a handler: the generated
  `BuildRenderTree` binds it with `RenderTreeBuilder.AddAttribute(sequence, "onclick", EventCallback.Factory.Create(this,
  handler))` (`http-in.component-event`, whose payloads are the name and the value). Each `on...` attribute whose value
  is made from a delegate is an entry point at `/_blazor`, the hub's default path, with no HTTP method and partial,
  as a SignalR hub method is; its handler is the method or lambda the delegate runs (`@onclick="Increment"`,
  `@onclick="() => Reset(0)"`, and the setter lambda `@bind` makes for `onchange`). Every component of the mapped
  assembly counts, whether or not it is rendered interactively; without interactive rendering, no event is. Source generators are given what the compiler gives them, the additional files
  (`/additionalfile:`, the `.razor` components) and the analyzer configuration (`/analyzerconfig:`, resolved from
  the project folder), so the Razor generator compiles the components and code that names them (`App` in
  `Program.cs`) binds. Generated code gives no units, so a component is attributed to its project's global
  namespace unit. Fixture: `CSharp.Blazor`.
- **SignalR hubs**: `MapHub<THub>(pattern)` is a registration (`http-in.map-hub`) whose `"hub-type"` payload is
  the index of the type argument that is the hub. SignalR is part of the shared framework, which is not followed,
  and invokes hub methods by name through reflection, so they are read from the hub's declaration, as MVC actions
  are: its public instance methods and those of its base classes outside the framework, except constructors,
  accessors, generic methods and overrides of the framework's virtual methods (`OnConnectedAsync`,
  `OnDisconnectedAsync`, `Dispose`). Each is an entry point at the pattern (with a route group's prefix), partial,
  as the invocation's target, not the HTTP method, chooses the handler; `[HubMethodName]` gives the name clients
  invoke it by (`http-in.signalr-hub-method-name`), shown in the evidence. Fixture: `CSharp.Endpoints/Chat.cs`. A
  SignalR client is the other side: a handler `On` registers (`network.signalr-client-on`) is a network entry point
  at the hub method the server invokes (a `"message-method"`, as Redis's subscription names a `"message-channel"`
  and a queue a `"message-queue"`), and `InvokeAsync`, `SendAsync` and `StreamAsync` are network exit points with the
  hub method's name (`network.signalr-client-call*`). Fixture: `CSharp.Clients` (`CSharp.Clients.Chat`).
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
  catch-all for unknown services and methods stays an entry point of the library. A gRPC client is the other
  side: the generated client calls a `CallInvoker` (`http-out.grpc-client`) with the same descriptor, so the call
  is an outbound HTTP call to `/{serviceName}/{methodName}` (the `"grpc-method"` payload). Code a generator wrote
  into the application (`[GeneratedCode]`) reports its calls to primitives at the application's call into it, where
  the effect is decided: the client call is an exit point in the method that calls `ReserveAsync`, not in each of
  the client's generated methods. Fixture: `CSharp.Grpc`.
- **Message consumers**: a handler a messaging library calls for each message it receives is an entry point of
  category `network`, whose route is the queue. A registration rule with a `"message-handler"` payload adds the
  delegate to a consumer (RabbitMQ's `Received` on `EventingBasicConsumer` and `AsyncEventingBasicConsumer`, Azure
  Service Bus's `ProcessMessageAsync`); the queue is read from the call that made the consumer, when its rule has
  a `"message-queue"` payload (`ServiceBusClient.CreateProcessor("payments")`), or from a call in the same method
  that ties the consumer to a queue, whose rule also has `"message-consumer"` (`BasicConsume("orders", autoAck,
  consumer)`: the consumer is whichever argument it is, as the overloads put it in different places, and
  `whenParameters` tells apart the overloads whose queue is elsewhere). A consumer kept in an async method's state
  machine is followed through its field. A method that receives in a loop of its own is a handler too: a rule
  with a `"message-receive"` payload names the receiving member (RabbitMQ's `BasicGet`, Service Bus's
  `ServiceBusReceiver.ReceiveMessage(s)Async`, Kafka's `Consume`), and the method that calls it is the entry point
  (an async method, not its state machine). The queue is the call's own `"message-queue"` (`BasicGet("audit")`),
  or is named where the receiver is made (`CreateReceiver("refunds")`) or subscribed (Kafka's `Subscribe(topic)`,
  a tying call). The entry point is complete when the queue is a constant, unresolved otherwise. A consumer type is read like a hosted service: MassTransit's `AddConsumer<T>()`
  (`network.masstransit-add-consumer`, whose `"consumer-type"` payload is the index of the type argument) and the
  callback rule `network.masstransit-consumer` (`IConsumer<TMessage>.Consume`) make each registered consumer's
  `Consume` an entry point, at the message type its parameter is a `ConsumeContext<T>` of. NServiceBus has no
  registration per handler: an endpoint finds its handlers by scanning the application. `Endpoint.Start` and
  `Endpoint.Create` (`network.nservicebus-endpoint`) have a `"scanned-handlers"` payload whose property names the
  callback type it looks for (`IHandleMessages<T>`, `network.nservicebus-handler`), and every type of the
  application that implements it has its `Handle` as an entry point at the message type of its first parameter.
  `Send`, `SendLocal` and `Publish` on a session or a handler's context are network exit points
  (`network.nservicebus-*`). Registrations of
  category `network` or `background` do not start the symbolic routing of HTTP requests. Fixture:
  `CSharp.Messaging`.
- **Azure Functions** (isolated worker): a method with `[Function("Name")]` (`http-in.functions-function`, role
  `"function-name"`) is run by the host when the trigger on one of its parameters fires: an attribute whose rule
  has a `"function-trigger"` payload, of the trigger's category. Parameter attributes are read for this
  (`MethodDef.ParameterAttributes`). An HTTP trigger (`http-in.functions-http-trigger`) gives every string among
  its arguments as the HTTP methods and its `Route` property (the function's name by default) under the
  `extensions.http.routePrefix` of the `host.json` next to the assembly (`api` by default): complete with
  methods, partial without. A timer (`background.functions-timer-trigger`) is background work at its schedule;
  queue, Service Bus, blob and Event Hubs triggers are of category `network` at their queue or path
  (`"trigger-address"`). Enum arguments of attributes, such as the trigger's `AuthorizationLevel`, are decoded
  with their types found next to the assembly. Fixture: `CSharp.Functions` (net8.0 only: the worker SDK it uses
  does not build for net10.0).
- **Azure Functions** (in-process model): the host loads a class library and runs each method with
  `[FunctionName("Name")]` (`http-in.webjobs-function-name`) the same way, its triggers in the WebJobs SDK's
  extensions (`http-in.webjobs-http-trigger`, `background.webjobs-timer-trigger`, `network.webjobs-*-trigger`).
  A project that sets `AzureFunctionsVersion` is an application in either model, though this one's output is a
  library (its structure is still compiled as one). Fixture: `CSharp.FunctionsInProcess` (net8.0 only).
- **Data clients**: clients of databases and storage services send their commands from connections of their
  own (a multiplexer, a connection pool, an HTTP pipeline of policies), which a call is followed to only by long,
  virtual routes: a MongoDB insert reached the database not at all, only low-confidence routes through its
  authentication code, and a blob upload was a hidden low-confidence HTTP call. Sink rules name the calls that run
  a command: StackExchange.Redis's `IDatabase` commands (`database.redis`, the key as payload), `Publish`
  (`network.redis-publish`) and `Subscribe`, whose handler is a network entry point at its channel
  (`network.redis-subscribe`); MongoDB's collection operations and the cursor methods that run a `Find` or an
  `Aggregate` (`database.mongodb-*`; `Find` itself only builds the query); and Azure Blob Storage's container and
  blob operations (`http-out.azure-blob*`, a blob's name as payload where the call takes one). An implicit
  conversion from a string (`op_Implicit(string)`, as to a `RedisKey`) keeps the string's value, and so does a
  type's own static factory from one string (`RedisChannel.Literal("cache")`). A sink rule can
  also match by attribute (`"markedCalls": true`): its type is an attribute, and a call to a method it marks is
  the effect, with the attribute's arguments as payload. Refit's verb attributes (`[Get("/products/{id}")]` on an
  interface method, `http-out.refit` on their base `HttpMethodAttribute`) make each call to such a method an HTTP
  call to its path. Email: MailKit sends on the connection `Connect` opened, which a call is not followed to, so
  `Connect` (with the host) and `Send` are network exit points (`network.mailkit-*`); SendGrid's `SendEmailAsync`
  is an HTTP one (`http-out.sendgrid`). A typed HTTP client's base address comes before its relative URLs:
  `AddHttpClient<TClient>(c => c.BaseAddress = new Uri(...))` (`http-out.typed-client`, the `"typed-http-client"`
  payload is the index of the type argument) gives the client type the constant its configuration delegate sets
  `BaseAddress` to (`http-out.base-address`), and an HTTP call the client makes through the `HttpClient` it keeps
  in a field (its constructor's parameter) has that address before a relative URL (`repos/{…}` is
  `https://api.github.com/repos/{…}`). A named client is joined by its name: `AddHttpClient("status", ...)`
  (`http-out.named-client`, the name a constant) and the client `IHttpClientFactory.CreateClient("status")` makes
  (`http-out.client-factory`). Fixture: `CSharp.Clients`.
- **Dapr**: the client sends every call to its sidecar, over gRPC or HTTP, which showed as dynamic gRPC calls
  (`CallInvoker.AsyncUnaryCall`) of medium confidence. Sink rules name the building blocks: the state store
  (`database.dapr-state`, the store's name), publishing (`network.dapr-publish`, the topic), output bindings
  (`network.dapr-binding`) and secret and configuration stores (`network.dapr-secrets`), by their names, and service
  invocation (`http-out.dapr-invoke*`), whose payload (`"app-method"`) is `http://{app id}/{method}`: from the two
  arguments, or, for `InvokeMethodAsync(request)`, from the `CreateInvokeMethodRequest` call that made the request
  (`"app-request"`). An `HttpClient` made by `DaprClient.CreateInvokeHttpClient("billing")` (`"invoked-app"`) has
  `http://billing` before its relative URLs. A local an async method keeps in its state machine is read as the value
  stored in the field (not the `null` it is cleared with). A subscription is a topic the sidecar posts to an endpoint
  of the application: a method with `[Topic(pubsub, topic)]` (`network.dapr-topic`, `"topic-subscription"`) and the
  handler of an endpoint `.WithTopic(pubsub, topic)` is added to (`network.dapr-with-topic`, `"subscribed-endpoint"`:
  the handler of the registration that made the builder, through other conventions) are network entry points at the
  topic (`"message-topic"`), besides their HTTP routes. Both are registered through `Dapr.TopicAttribute`, which
  `WithTopic` adds to the endpoint's metadata, so the architecture names Dapr as what delivers the messages. Not read: bulk subscriptions, actors, workflows, jobs and the
  distributed lock. Fixture: `CSharp.Dapr`.
- **Orleans grains**: a silo activates grains and runs the calls clients make to them, through proxies and
  invokers Orleans's source generator writes into the application, so a grain method had no caller and a call
  to a grain went nowhere. `UseOrleans` (`network.orleans-silo`) is a registration with a `"scanned-grains"`
  payload: every class of the application and the user assemblies it references that implements an interface
  derived from `Orleans.IGrain` (the callback rule `network.orleans-grain`) is a grain, and each method of such an
  interface it implements is a network entry point at `Interface.Method` (`CSharp.Silo.Orders.IOrderGrain.Place`),
  complete, registered through `Orleans.IGrain`. A call to such a method is a network exit point at the same
  address (`network.orleans-grain-call`, payload `"grain-method"`), so an application that calls another's grain
  is linked to it in the architecture. That rule has `"applicationInterfaces": true`: it matches the methods of
  interfaces declared in the solution only, not Orleans's own system grains. Generated code is told by
  `[GeneratedCode]` on a method or on its type (Orleans marks the classes): the proxies are no grains, and the
  invokers' calls to the grain are not reported. `RegisterGrainTimer` and `Grain.RegisterTimer`
  (`background.orleans-*timer`) make the callback background work; `IStorage`'s `ReadStateAsync`,
  `WriteStateAsync` and `ClearStateAsync` (`database.orleans-grain-state`) are a grain's persistent state, without
  a payload: the storage provider is named in a `[PersistentState]` attribute on a constructor parameter, which is
  not read. Not read either: streams (implicit and explicit subscriptions), reminders, observers, and the
  storage provider behind the state. Fixture: `CSharp.Orleans` (silo and client in one host).
- **WCF services and clients**: CoreWCF dispatches a message to a service by its action, through code of its own
  (and invokers its source generator writes), so a service's methods had no caller; a System.ServiceModel client
  sends through its channel stack, which only reached low-confidence guesses deep inside the library.
  `AddServiceEndpoint<TService, TContract>(binding, address)` (`network.corewcf-endpoint`, payloads
  `"wcf-service-type"` and `"wcf-contract-type"`: type argument indices) exposes a contract: each method marked
  `[OperationContract]` of the contract and of the contracts it derives from is a network entry point at its
  action, handled by the service's implementation (explicit or by name), complete, registered through the
  contract's `[ServiceContract]` attribute (CoreWCF's or System.ServiceModel's, the `"wcf-contract"` role). The
  action is the operation's `Action`, else `{Namespace}/{Name}/{operation}`: the contract's namespace
  (`http://tempuri.org/` by default), its name (the interface's by default) and the operation's (the method's,
  without the `Async` of a method returning a task), as WCF names it. A client calls a method of its own copy of
  the contract, marked with System.ServiceModel's `[OperationContract]` (`network.wcf-operation-call`, a marked
  call, payload `"wcf-action"`): a network exit point at the same action, so the client is linked to the service
  in the architecture whatever the binding. The call is reported where the application makes it, through a channel
  (`ChannelFactory<T>.CreateChannel`) or a proxy: a class's method that implements a marked interface method is a
  proxy's when it passes the call on to a marked method (a `ClientBase<T>` proxy calls its `Channel`'s) or its
  class is not the user's (`[GeneratedCode]`, as dotnet-svcutil writes, or `[DebuggerNonUserCode]`, as Refit's
  generator does). A call to it is the call to the interface method, and nothing is reported inside. Not read:
  the endpoint address beyond the evidence, the non-generic `AddServiceEndpoint` overloads (contract as a `Type`),
  endpoints from configuration files, duplex callbacks and the binding. Fixtures: `CSharp.Wcf` (service) and
  `CSharp.WcfClient`.
- **OpenTelemetry exporters**: an exporter sends what traces, metrics and logs record from a worker of its own,
  reached through the processors and metric readers of every provider, which call `BaseExporter<T>.Export` on
  whatever exporters were constructed: every setup call (`WithTracing`, `AddOpenTelemetry`) showed every
  exporter's sending, file storage for retries and host name lookup as low- or medium-confidence guesses. Adding
  an exporter is the exit point now, at the call: `AddOtlpExporter` for traces, metrics and logs, `UseOtlpExporter`,
  `AddZipkinExporter` and Azure Monitor's `UseAzureMonitor`, `UseAzureMonitorExporter` and
  `AddAzureMonitor*Exporter` are HTTP calls (`http-out.otlp-*`, `http-out.zipkin-exporter`,
  `http-out.azure-monitor*`); `AddConsoleExporter` writes to the console (`environment.otel-console-*`). The
  address is the endpoint the options' configuration delegate sets (`o => o.Endpoint = new Uri(...)`, the payload
  role `"configured-url"`: the value the delegate among the call's arguments gives the rule's property) or
  `UseOtlpExporter`'s `Uri` argument. Without one it is `OTEL_EXPORTER_OTLP_ENDPOINT`'s or the default, and is left
  unknown. `BaseExporter<T>.Export` is not followed in libraries' code (`unfollowed.otel-export`, the rule kind
  `"unfollowed"`): it is the effect of the call that added the exporter. Prometheus is scraped rather than sending:
  `AddPrometheusExporter` is no exit point, and the scraping endpoint (`MapPrometheusScrapingEndpoint`) is not
  read as an entry point. Not read either: an Azure Monitor connection string's ingestion endpoint, exporters
  added as processors by hand (`AddProcessor(new BatchActivityExportProcessor(new OtlpTraceExporter(...)))`) and
  options bound from configuration. Fixture: `CSharp.Telemetry`.
- **AWS and Azure Cosmos DB**: the AWS SDK for .NET and the Cosmos DB client send requests through pipelines of

  handlers of their own, past credential providers that read files, start processes and call the platform's
  crypto API, so a call showed as low- or medium-confidence HTTP among that noise. Sink rules name the operations:
  S3's object and bucket operations (`http-out.aws-s3`, the request's `BucketName`), DynamoDB's item, query and
  table operations (`database.aws-dynamodb`, `TableName`), SQS's `SendMessage*` (`network.aws-sqs-send*`, the
  queue URL as an argument or the request's `QueueUrl`), and Cosmos DB's container and item operations, a query
  iterator's `ReadNextAsync` and a transactional batch's `ExecuteAsync` (`database.cosmos-*`). A loop that calls
  SQS's `ReceiveMessage*` is a network entry point at its queue (`network.aws-sqs-receive*`; a `message-queue`
  can now be a property of an argument). Constructing a client still shows its credential lookup. Elasticsearch's
  calls were low-confidence HTTP through the client's transport; `database.elasticsearch` names the request methods
  of `ElasticsearchClient`. Fixture: `CSharp.Cloud` (AWS, Cosmos DB, Elasticsearch).
- **Sending messages**: the messaging libraries write a message from their own loops (RabbitMQ's frame writer,
  Kafka's native library, a bus's transport), which a call is not followed to: publishing showed only the
  connection's DNS lookup. Sink rules of category `network` name the calls that send, as for Entity Framework
  Core: RabbitMQ's `BasicPublish` (the routing key is the payload; `whenParameters` tells the overloads apart),
  Kafka's `Produce`/`ProduceAsync` (the topic), Azure Service Bus's `ServiceBusSender.Send*`/`Schedule*`, and
  MassTransit's `Publish` and `Send`. Fixture: `CSharp.Messaging` (`CSharp.Messaging.Publishing`, and a loop over
  Kafka's `Consume` in `CSharp.Messaging.Refunds`).
- **Hosted services**: background work the generic host starts by itself is an entry point of category
  `background`. `AddHostedService<T>()` is a registration (`background.add-hosted-service`) whose
  `"hosted-service-type"` payload is the index of the type argument that is the service, and callback rules of
  category `background` name what the host calls: `IHostedService.StartAsync` and `StopAsync`
  (`background.hosted-service`), a `BackgroundService`'s `ExecuteAsync` (`background.background-service`). Each
  such member the application implements is an entry point, with no route; its trace is what the work does. A
  hosted service the application never registers is not one. A timer's callback is background work too: the
  registration rules `background.threading-timer` (the `System.Threading.Timer` constructor, read where it is
  constructed) and `background.timers-timer-elapsed` (`System.Timers.Timer.Elapsed`) name the delegate with the
  same `"message-handler"` role as message consumers, and the entry point takes the rule's category. A Quartz.NET
  job is read like a hosted service: `AddJob<T>()` and `ScheduleJob<T>()` (`background.quartz-add-job`, whose
  `"job-type"` payload is the index of the type argument) and the callback rule `background.quartz-job`
  (`IJob.Execute`) make each registered job's `Execute` background work. A file watcher's handler
  (`FileSystemWatcher`'s `Changed`, `Created`, `Deleted`, `Renamed`; `filesystem-read.watcher-events`) is an input
  from the file system: an entry point of category `filesystem-read`, at the path the watcher's constructor is
  given (`filesystem-read.watcher-path`, a constructor that names the consumer's address). A Hangfire job is
  handed over as an expression tree (`RecurringJob.AddOrUpdate<Reports>("nightly", r => r.Nightly(), ...)`,
  `BackgroundJob.Enqueue`, and the client and manager extension methods; `background.hangfire-*`), whose method
  its server runs: the `"expression-handler"` role finds the method by the token the tree's construction loads for
  `Expression.Call` (the IL reader now keeps a method token's method, `LdToken`), and that method is background
  work. A recurring job is at its cron expression (`"job-schedule"`), whose index depends on the overload: the
  rules are split by the parameters before the job (`whenParameters`), the longer prefixes first. The expression
  is read through Hangfire's own `Cron` helpers (`Cron.Daily()` calls `Daily(0)`, which calls `Daily(0, 0)`, which
  formats its arguments: `0 0 * * *`); a helper handed over as a method group (`Cron.Hourly`, a `Func<string>`) is
  what the method returns. A schedule that is not constant leaves the entry unresolved. A channel
  (`System.Threading.Channels`) hands items from one part of the application to another: a method that reads one
  (`ChannelReader<T>`'s `ReadAsync`, `ReadAllAsync`, `TryRead`, `WaitToReadAsync`; `background.channel-read`, a
  `"message-receive"`) is background work at the type of the items, its `"channel-item"`: the index of the reader's
  type argument, which the IL reader now keeps for a member of a generic type instance
  (`MethodRef.DeclaringTypeArguments`; summaries cache key v20). A hosted service that reads a channel is that
  entry, at the items' type, in place of its own. When the items are delegates (the queue of queued background
  tasks in ASP.NET Core's docs) the reader only fetches the work: each delegate written to the channel
  (`ChannelWriter<T>`'s `WriteAsync` and `TryWrite`, `background.channel-write`, `"queued-work"`) is background
  work of its own, followed back through the parameters of the methods that pass it on (an async method's through
  the field of its state machine that holds the parameter), with a step for each. Writing to a channel is no exit
  point, and the delegate's effects stay in the trace of the method that makes it, as a timer's callback's do.
  Fixture: `CSharp.Worker` (Microsoft.Extensions.Hosting as a package, on the Worker SDK).
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
- **Names read from configuration**: a queue, URL or other payload read from
  Microsoft.Extensions.Configuration by a constant key has the value the output folder's `appsettings.json` gives
  it, with `appsettings.Production.json` over it as in the default environment. Catalog rules of a new kind,
  `setting`, say what a call reads (`"setting": { "reads": ... }`): the value at a key (`configuration["Queues:Orders"]`,
  `GetValue<T>`, `GetConnectionString`, under `"prefix": "ConnectionStrings"`), a section (`GetSection`,
  `GetRequiredSection`) and a section's own `Value`, an options object (`IOptions<T>.Value`,
  `IOptionsMonitor<T>.CurrentValue`), and the binds that tie an options type to a section in the project's
  code (`Configure<T>(section)`, `AddOptions<T>().Bind(section)`, `BindConfiguration(path)`; a type bound to two
  sections is bound to neither). A property of an object inside an options object is at its path under the
  section (`options.Value.Rabbit.Queue` at `Section:Rabbit:Queue`), and an options object, or an object inside
  one, kept in a field (`_options = options.Value` in a constructor) is found again where the field is read:
  a field every store gives the same key, in the methods that read options, is at that key. A key the files do
  not have, environment variables and other configuration
  sources stay dynamic. The symbolic run that finds registrations reads them too, so a minimal API route named
  in the configuration has its template. Fixtures: `CSharp.Messaging` (`CSharp.Messaging.Configured`), with
  `appsettings.json` and `appsettings.Production.json`, and `CSharp.Endpoints` (`Configured.cs`).

## Not done yet

- Generators that read additional files or MSBuild properties through analyzer config options get none, so code
  depending on what those generate may not bind; it is then reported as a compile error and the result is not
  cached.
