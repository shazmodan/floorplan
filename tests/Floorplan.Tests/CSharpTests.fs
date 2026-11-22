/// C# support (plan Phase 7): the routing styles of the Routing fixture written in C#, and the recognizer for
/// C# compiler output.
module Floorplan.Tests.CSharpTests

open Expecto
open Floorplan.Model
open Floorplan.IL
open Floorplan.IL.Codegen.CSharp
open Floorplan.Cli

let csharpModel = lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.csharp.Force()))

let private csharpModelNet10 =
    lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.csharpNet10.Force()))

let private entries (model: AnalysisModel) (app: string) =
    model.EntryPoints |> List.filter (fun e -> e.ApplicationId = app)

let private field (typeName: string) (name: string) =
    { Type = { Assembly = "App"; FullName = typeName }
      Name = name
      FieldType = "System.Func`1<System.String>" }

[<Tests>]
let tests =
    testSequenced
    <| testList
        "C#"
        [ test "golden: CSharp fixture" { Golden.check "csharp.entry-points.txt" (EntryPointTests.render (csharpModel.Force())) }

          test "golden: CSharp fixture on net10.0 gives the net8.0 results" {
              let m = csharpModelNet10.Force()
              // CSharp.Functions targets net8.0 only: the Functions worker SDK it uses does not build for net10.0.
              Expect.isTrue
                  (m.Projects |> List.forall (fun p -> p.TargetFramework = "net10.0" || p.Name = "CSharp.Functions"))
                  "every project that targets net10.0 on net10.0"
              Golden.check "csharp.entry-points.txt" (EntryPointTests.render m)
          }

          test "golden: CSharp exit points" { Golden.check "csharp.exit-points.txt" (ExitPointTests.render (csharpModel.Force())) }

          test "golden: CSharp exit points on net10.0 give the net8.0 results" {
              Golden.check "csharp.exit-points.txt" (ExitPointTests.render (csharpModelNet10.Force()))
          }

          test "Razor Pages: each page model handler is an entry point at the page's route" {
              let m = csharpModel.Force()

              let pages =
                  entries m "CSharp.Controllers"
                  |> List.filter (fun e -> e.HandlerMember.Contains ".Pages.Shop." || e.HandlerMember.StartsWith "AspNetCoreGeneratedDocument.")
                  |> List.map (fun e ->
                      e.HttpMethod,
                      e.Route,
                      e.HandlerMember,
                      e.Predicates |> List.filter (fun p -> p.Part = RequestPart.Query) |> List.map (fun p -> p.Value))
                  |> List.sort

              Expect.equal
                  pages
                  [ Some "GET", Some "/Admin/Audit", "AspNetCoreGeneratedDocument.Areas_Admin_Pages_Audit::ExecuteAsync", []
                    Some "GET", Some "/Shop", "CSharp.Controllers.Pages.Shop.IndexModel::OnGet", []
                    Some "GET", Some "/Shop/Index", "CSharp.Controllers.Pages.Shop.IndexModel::OnGet", []
                    Some "GET", Some "/Shop/Item/{id:int}", "CSharp.Controllers.Pages.Shop.ItemModel::OnGetAsync", []
                    Some "POST", Some "/Shop/Item/{id:int}", "CSharp.Controllers.Pages.Shop.ItemModel::OnPostRemove", [ "handler=Remove" ] ]
                  "an Index page at its folder too, a template after the path, a named handler, an area page without a model"
          }

          test "Blazor: each @page of a routed component is a GET entry point" {
              let m = csharpModel.Force()

              Expect.equal
                  (entries m "CSharp.Blazor"
                   |> List.filter (fun e -> e.HttpMethod.IsSome)
                   |> List.map (fun e -> e.HttpMethod, e.Route, e.HandlerMember, e.Completeness)
                   |> List.sort)
                  [ Some "GET", Some "/", "CSharp.Blazor.Components.Pages.Home::BuildRenderTree", Completeness.Complete
                    Some "GET", Some "/counter", "CSharp.Blazor.Components.Pages.Counter::BuildRenderTree", Completeness.Complete
                    Some "GET", Some "/order/{Id:int}", "CSharp.Blazor.Components.Pages.Order::OnInitializedAsync", Completeness.Complete
                    Some "GET", Some "/orders/{Id:int}", "CSharp.Blazor.Components.Pages.Order::OnInitializedAsync", Completeness.Complete ]
                  "two @page directives, a page without lifecycle methods; not a component without @page"
          }

          test "Blazor: an interactive component's events are entry points at the circuit's hub" {
              let m = csharpModel.Force()

              let events =
                  entries m "CSharp.Blazor" |> List.filter (fun e -> e.HttpMethod.IsNone)

              Expect.isTrue
                  (events |> List.forall (fun e -> e.Route = Some "/_blazor" && e.Completeness = Completeness.Partial))
                  "at the hub's path; the circuit's message, not the request, chooses the handler"

              let bound =
                  events
                  |> List.collect (fun e -> e.Evidence.Steps |> List.choose (fun s -> if s.Description.Contains " binds its " then Some(e.HandlerMember, s.Description) else None))

              let boundTo (event: string) =
                  bound
                  |> List.filter (fun (_, d) -> d.StartsWith $"CSharp.Blazor.Components.Pages.Counter binds its \"{event}\" event")
                  |> List.map fst
                  |> List.sort

              Expect.equal (boundTo "onclick").Length 2 "@onclick, to a method and to a lambda"
              Expect.contains (boundTo "onclick") "CSharp.Blazor.Components.Pages.Counter::Increment" "a method"
              Expect.equal (boundTo "onchange").Length 1 "@bind's setter"
              Expect.equal events.Length 3 "no other events"

              Expect.exists
                  m.ExitPoints
                  (fun x -> x.Member = "CSharp.Blazor.Components.Pages.Counter::Increment" && x.Category = EffectCategory.FilesystemWrite)
                  "what the handler does"
          }

          test "SignalR: each hub method clients can invoke is an entry point at the hub's pattern" {
              let m = csharpModel.Force()

              let hub =
                  entries m "CSharp.Endpoints"
                  |> List.filter (fun e -> e.HandlerMember.StartsWith "CSharp.Endpoints.Chat.")
                  |> List.map (fun e -> e.HandlerMember, e.Route, e.Completeness)
                  |> List.sort

              Expect.equal
                  hub
                  [ "CSharp.Endpoints.Chat.ChatHub::LeaveRoom", Some "/hubs/chat", Completeness.Partial
                    "CSharp.Endpoints.Chat.ChatHub::Send", Some "/hubs/chat", Completeness.Partial
                    "CSharp.Endpoints.Chat.RoomHub::Ping", Some "/hubs/chat", Completeness.Partial ]
                  "public methods, inherited ones too; not the platform's OnConnectedAsync, nor private ones"

              let leave = entries m "CSharp.Endpoints" |> List.find (fun e -> e.HandlerMember.EndsWith "::LeaveRoom")
              Expect.exists leave.Evidence.Steps (fun s -> s.Description.Contains "invoke as \"leave\"") "[HubMethodName]"

              // The trace of a hub method reaches what it does.
              let send = entries m "CSharp.Endpoints" |> List.find (fun e -> e.HandlerMember.EndsWith "::Send")
              let trace = m.Traces |> List.find (fun t -> t.EntryPointId = send.Id)

              Expect.exists
                  (m.ExitPoints |> List.filter (fun x -> List.contains x.Id trace.ExitPointIds))
                  (fun x -> x.Primitive = "System.IO.File::AppendAllTextAsync")
                  "Send writes the chat log"
          }

          test "gRPC: each method a service overrides is POST /{service}/{method}, read from the generated binding" {
              let m = csharpModel.Force()

              let services =
                  entries m "CSharp.Grpc"
                  |> List.filter (fun e -> e.HandlerMember.StartsWith "CSharp.Grpc.")
                  |> List.map (fun e -> e.HttpMethod, e.Route, e.Completeness, e.HandlerMember)

              Expect.equal
                  services
                  [ Some "POST", Some "/shop.inventory.Stock/Reserve", Completeness.Complete, "CSharp.Grpc.Inventory.StockService::Reserve" ]
                  "Reserve is implemented; Release is left to the generated base class, which answers unimplemented"

              let reserve = entries m "CSharp.Grpc" |> List.find (fun e -> e.HandlerMember.EndsWith "::Reserve")
              let trace = m.Traces |> List.find (fun t -> t.EntryPointId = reserve.Id)

              Expect.exists
                  (m.ExitPoints |> List.filter (fun x -> List.contains x.Id trace.ExitPointIds))
                  (fun x -> x.Primitive = "System.IO.File::AppendAllTextAsync")
                  "Reserve writes the reservations log"

              // A client: the call into the generated client is the outbound call, at the application's call site.
              let outbound =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Category = EffectCategory.HttpOut && x.Unit.StartsWith "CSharp.Grpc:")
                  |> List.map (fun x -> x.Member, x.Payload |> Option.bind (fun p -> p.Value), x.Confidence)

              Expect.equal
                  outbound
                  [ "CSharp.Grpc.Restocking.Restocker/<Reserve>d__1::MoveNext", Some "/shop.inventory.Stock/Reserve", Confidence.High ]
                  "one call, where the application makes it; nothing inside the generated client"
          }

          test "message consumers: each handler a messaging library calls is a network entry point at its queue" {
              let m = csharpModel.Force()

              let consumers =
                  entries m "CSharp.Messaging"
                  |> List.filter (fun e -> not (e.HandlerMember.StartsWith "CSharp.Messaging.Configured."))
                  |> List.map (fun e -> e.Route, e.Category, e.Completeness, e.HandlerMember)
                  |> List.sort

              Expect.equal
                  consumers
                  [ Some "CSharp.Messaging.Sales.PlaceOrder", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Sales.PlaceOrderHandler::Handle"
                    Some "CSharp.Messaging.Shipping.OrderShipped", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Shipping.ShippedConsumer::Consume"
                    Some "audit", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Refunds.Poller::Drain"
                    Some "orders", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Orders.Listener/<>c::<Start>b__0_0"
                    Some "payments", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Payments.Listener::Handle"
                    Some "refunds", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Refunds.Poller::RunAsync"
                    Some "returns", EffectCategory.Network, Completeness.Complete, "CSharp.Messaging.Orders.Listener::OnReturn" ]
                  "RabbitMQ's Received with BasicConsume (two overloads), Service Bus's ProcessMessageAsync with CreateProcessor, MassTransit's AddConsumer at its message type, an NServiceBus handler found by scanning; not the error handler, nor a consumer never registered"

              let traced (handler: string) =
                  let e = entries m "CSharp.Messaging" |> List.find (fun e -> e.HandlerMember = handler)
                  let t = m.Traces |> List.find (fun t -> t.EntryPointId = e.Id)
                  m.ExitPoints |> List.filter (fun x -> List.contains x.Id t.ExitPointIds) |> List.map (fun x -> x.Primitive)

              Expect.contains (traced "CSharp.Messaging.Payments.Listener::Handle") "System.IO.File::AppendAllTextAsync" "a payment is logged"
              Expect.contains (traced "CSharp.Messaging.Orders.Listener::OnReturn") "System.IO.File::Delete" "a return deletes its pending file"
          }

          test "a minimal API route named in appsettings.json: by key, through a section, and through bound options" {
              let m = csharpModel.Force()

              let routes =
                  entries m "CSharp.Endpoints"
                  |> List.filter (fun e -> e.HandlerMember.StartsWith "CSharp.Endpoints.Configured.")
                  |> List.map (fun e -> e.HttpMethod, e.Route, e.Completeness)
                  |> List.sort

              Expect.equal
                  routes
                  [ Some "GET", Some "/configured/health", Completeness.Complete
                    Some "GET", Some "/configured/status", Completeness.Complete
                    Some "GET", Some "/configured/version", Completeness.Complete ]
                  "app.Configuration[key], a section's indexer, IOptions<T>.Value of Configure<T>(section)"
          }

          test "a queue named in appsettings.json: by key, through a section, with GetValue, and through bound options" {
              let m = csharpModel.Force()

              let consumers =
                  entries m "CSharp.Messaging"
                  |> List.filter (fun e -> e.HandlerMember.StartsWith "CSharp.Messaging.Configured.")
                  |> List.map (fun e -> e.Route, e.Completeness)
                  |> List.sort

              Expect.equal
                  consumers
                  [ Some "audit-log", Completeness.Complete
                    Some "credits", Completeness.Complete
                    Some "invoices", Completeness.Complete
                    Some "reminders", Completeness.Complete ]
                  "configuration[key], GetSection(...).GetSection(...).Value, GetValue<string> (appsettings.Production.json overrides), IOptions<T>.Value of Configure<T>(section)"

              let published (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = memberName && x.Category = EffectCategory.Network)
                  |> List.map (fun x -> x.Payload |> Option.bind (fun p -> p.Value))

              Expect.equal (published "CSharp.Messaging.Configured.Invoices::Notify") [ Some "invoices" ] "a publish to a configured queue"
              Expect.equal (published "CSharp.Messaging.Configured.Invoices::Missing") [ None ] "a key the configuration does not have"
          }

          test "Azure Functions: each function is an entry point of its trigger's kind, HTTP routes under host.json's prefix" {
              let m = csharpModel.Force()

              let functions =
                  entries m "CSharp.Functions"
                  |> List.map (fun e -> e.HandlerMember, e.Category, e.HttpMethod, e.Route, e.Completeness)
                  |> List.sort

              Expect.equal
                  functions
                  [ "CSharp.Functions.Maintenance.Cleanup::Run", EffectCategory.Background, None, Some "0 */5 * * * *", Completeness.Complete
                    "CSharp.Functions.Orders.OrderFunctions::Get", EffectCategory.HttpIn, Some "GET", Some "/fn/orders/{id}", Completeness.Complete
                    "CSharp.Functions.Orders.OrderFunctions::OnOrder", EffectCategory.Network, None, Some "orders", Completeness.Complete
                    "CSharp.Functions.Orders.OrderFunctions::Place", EffectCategory.HttpIn, Some "POST,PUT", Some "/fn/PlaceOrder", Completeness.Complete ]
                  "HTTP with a route and without one, a timer, a queue; not a method without [Function]"

              let get = entries m "CSharp.Functions" |> List.find (fun e -> e.HandlerMember.EndsWith "::Get")
              let trace = m.Traces |> List.find (fun t -> t.EntryPointId = get.Id)

              Expect.exists
                  (m.ExitPoints |> List.filter (fun x -> List.contains x.Id trace.ExitPointIds))
                  (fun x -> x.Primitive = "System.IO.File::AppendAllText")
                  "GetOrder logs the read"
          }

          test "an application nothing starts but the process has its entry point where the process starts" {
              let m = csharpModel.Force()

              Expect.equal
                  (entries m "CSharp.Effects" |> List.map (fun e -> e.HandlerMember, e.Category, e.Route, e.Completeness))
                  [ "Program::<Main>$", EffectCategory.Background, None, Completeness.Complete ]
                  "CSharp.Effects: requests, hosts and libraries start nothing in it"

              Expect.isFalse
                  (m.EntryPoints |> List.exists (fun e -> e.ApplicationId <> "CSharp.Effects" && e.HandlerMember.EndsWith "::<Main>$"))
                  "an application with other entry points has none at its start"
          }

          test "AWS: S3 is HTTP, DynamoDB a database, SQS messaging, each with what its request names" {
              let m = csharpModel.Force()

              let named (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = memberName && x.Confidence = Confidence.High && x.Category <> EffectCategory.Nondeterminism)
                  |> List.map (fun x -> x.Category, x.Primitive.Substring(x.Primitive.LastIndexOf '.' + 1), x.Payload |> Option.bind (fun p -> p.Value))

              Expect.equal (named "CSharp.Cloud.Reports.Archive::Store") [ EffectCategory.HttpOut, "IAmazonS3::PutObjectAsync", Some "reports" ] "S3, the bucket"
              Expect.equal (named "CSharp.Cloud.Orders.Table::Save") [ EffectCategory.Database, "IAmazonDynamoDB::PutItemAsync", Some "orders" ] "DynamoDB, the table"

              Expect.equal
                  (named "CSharp.Cloud.Jobs.Queue::Enqueue")
                  [ EffectCategory.Network, "IAmazonSQS::SendMessageAsync", Some "https://sqs.eu-north-1.amazonaws.com/123456789012/jobs" ]
                  "SQS, the queue"

              Expect.equal
                  (entries m "CSharp.Cloud" |> List.map (fun e -> e.HandlerMember, e.Category, e.Route))
                  [ "CSharp.Cloud.Jobs.Queue::Run", EffectCategory.Network, Some "https://sqs.eu-north-1.amazonaws.com/123456789012/jobs" ]
                  "a loop that receives from SQS"
          }

          test "data clients: Redis and MongoDB commands are database calls, Azure Blob Storage and Refit calls are HTTP" {
              let m = csharpModel.Force()

              let calls (typeName: string) =
                  m.ExitPoints
                  |> List.filter (fun x ->
                      x.Member.StartsWith $"CSharp.Clients.{typeName}"
                      && x.Confidence = Confidence.High
                      && (x.Category = EffectCategory.Database || x.Category = EffectCategory.HttpOut || x.Category = EffectCategory.Network))
                  |> List.map (fun x -> x.Category, x.Primitive.Substring(x.Primitive.LastIndexOf '.' + 1), x.Payload |> Option.bind (fun p -> p.Value))
                  |> List.sort

              Expect.equal
                  (calls "Caching.Cache")
                  [ EffectCategory.Network, "IDatabaseAsync::PublishAsync", Some "cache"
                    EffectCategory.Database, "IDatabase::StringGet", None
                    EffectCategory.Database, "IDatabase::StringIncrement", Some "visits"
                    EffectCategory.Database, "IDatabaseAsync::StringSetAsync", None ]
                  "Redis: commands with their key, a publish on its channel (RedisChannel.Literal)"

              Expect.contains
                  (entries m "CSharp.Clients" |> List.map (fun e -> e.Route, e.Completeness))
                  (Some "cache", Completeness.Complete)
                  "Redis: a subscription on its channel"

              Expect.equal
                  (calls "Documents.Store")
                  [ EffectCategory.Database, "IAsyncCursorSourceExtensions::ToListAsync", None
                    EffectCategory.Database, "IMongoCollectionExtensions::DeleteOneAsync", None
                    EffectCategory.Database, "IMongoCollection`1::InsertOneAsync", None ]
                  "MongoDB: a find runs where its cursor is read"

              Expect.equal
                  (calls "Files.Archive")
                  [ EffectCategory.HttpOut, "BlobBaseClient::DownloadContentAsync", None
                    EffectCategory.HttpOut, "BlobContainerClient::UploadBlobAsync", Some "archive/{…}" ]
                  "Azure Blob Storage, with the blob's name"

              Expect.equal
                  (calls "Catalog.Products")
                  [ EffectCategory.HttpOut, "ICatalogApi::AddProduct", Some "/products"
                    EffectCategory.HttpOut, "ICatalogApi::GetProduct", Some "/products/{id}" ]
                  "Refit: a call to a method marked with a verb is an HTTP call to its path"

              Expect.equal
                  (calls "Notifications.Mail")
                  [ EffectCategory.HttpOut, "BaseClient::SendEmailAsync", None
                    EffectCategory.Network, "MailService::ConnectAsync", Some "smtp.example.org"
                    EffectCategory.Network, "MailTransport::SendAsync", None ]
                  "email: MailKit's connection and send, SendGrid's request"

              Expect.equal
                  (calls "GitHub.GitHubClient")
                  [ EffectCategory.HttpOut, "HttpClient::GetStringAsync", Some "https://api.github.com/repos/{…}"
                    EffectCategory.HttpOut, "HttpClient::GetStringAsync", Some "https://api.github.com/zen" ]
                  "a typed client's relative URLs, under the base address AddHttpClient gives it"

              Expect.equal
                  (calls "GitHub.Status")
                  [ EffectCategory.HttpOut, "HttpClient::GetStringAsync", Some "https://status.example.org/health" ]
                  "a named client's, under the base address registered with its name"

              // A SignalR client: the server invokes the handlers On registers; InvokeAsync calls a hub method.
              Expect.equal
                  (calls "Chat.Room")
                  [ EffectCategory.Network, "HubConnectionExtensions::InvokeAsync", Some "Send" ]
                  "a hub method, by its name"

              Expect.exists
                  (entries m "CSharp.Clients")
                  (fun e -> e.Category = EffectCategory.Network && e.Route = Some "received" && e.HandlerMember.StartsWith "CSharp.Clients.Chat.Room/<>c::<Listen>")
                  "a handler the server invokes, at its hub method"

              // Redis pub/sub: the subscriber's handler is a network entry point.
              Expect.exists
                  (entries m "CSharp.Clients")
                  (fun e -> e.Category = EffectCategory.Network && e.HandlerMember.StartsWith "CSharp.Clients.Caching.Cache/<>c::<Listen>")
                  "a Redis subscription"
          }

          test "sending a message is a network exit point, with the routing key where the call has one" {
              let m = csharpModel.Force()

              let sent (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = $"CSharp.Messaging.Publishing.Publisher::{memberName}" && x.Category = EffectCategory.Network)
                  |> List.map (fun x -> x.Primitive, x.Payload |> Option.bind (fun p -> p.Value), x.Confidence)

              Expect.equal (sent "Order") [ "RabbitMQ.Client.IModelExensions::BasicPublish", Some "orders", Confidence.High ] "RabbitMQ, to the routing key"
              Expect.equal (sent "Payment") [ "Azure.Messaging.ServiceBus.ServiceBusSender::SendMessageAsync", None, Confidence.High ] "Service Bus"
              Expect.equal (sent "Shipped") [ "MassTransit.IPublishEndpoint::Publish", None, Confidence.High ] "MassTransit"
          }

          test "hosted services, timers and scheduled jobs: what a host, a timer or a scheduler starts is background work, an entry point of its own" {
              let m = csharpModel.Force()

              let worker =
                  entries m "CSharp.Worker"
                  |> List.map (fun e -> e.HandlerMember, e.Category, e.Route, e.Completeness)
                  |> List.sort

              Expect.equal
                  worker
                  [ "CSharp.Worker.Cleanup.PurgeJob::Execute", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Cleanup.Sweeper::ExecuteAsync", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Imports.Inbox/<>c::<Watch>b__0_0", EffectCategory.FilesystemRead, Some "inbox", Completeness.Complete
                    "CSharp.Worker.Monitoring.Heartbeat/<>c::<StartAsync>b__3_0", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Monitoring.Heartbeat::OnAudit", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Monitoring.Heartbeat::StartAsync", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Monitoring.Heartbeat::StopAsync", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Reporting.Reports::Nightly", EffectCategory.Background, None, Completeness.Complete
                    "CSharp.Worker.Reporting.Reports::Send", EffectCategory.Background, None, Completeness.Complete ]
                  "a BackgroundService's ExecuteAsync, an IHostedService's StartAsync and StopAsync, the callbacks of a System.Threading.Timer and a System.Timers.Timer, a Quartz job, Hangfire jobs (by the method their expression calls), and a file watcher's handler (an input from the file system, at the path it watches); not a service never registered"

              let audit = entries m "CSharp.Worker" |> List.find (fun e -> e.HandlerMember.EndsWith "::OnAudit")
              Expect.exists audit.Evidence.Steps (fun s -> s.Description.Contains "called each time it fires") "a timer's callback"

              let traced (handler: string) =
                  let e = entries m "CSharp.Worker" |> List.find (fun e -> e.HandlerMember = handler)
                  let t = m.Traces |> List.find (fun t -> t.EntryPointId = e.Id)
                  m.ExitPoints |> List.filter (fun x -> List.contains x.Id t.ExitPointIds) |> List.map (fun x -> x.Category)

              Expect.contains (traced "CSharp.Worker.Cleanup.Sweeper::ExecuteAsync") EffectCategory.FilesystemWrite "the sweep deletes files"
              Expect.contains (traced "CSharp.Worker.Monitoring.Heartbeat::StartAsync") EffectCategory.HttpOut "the heartbeat calls out"
          }

          test "what a logger's sinks do is one exit point per unit, listing its log calls" {
              let m = csharpModel.Force()

              let logged (unitName: string) =
                  m.ExitPoints
                  |> List.filter (fun x ->
                      x.Unit = $"CSharp.Effects:CSharp.Effects.{unitName}" && x.ViaAssembly = Some "Serilog")

              let writes = logged "Logging" |> List.filter (fun x -> x.Category = EffectCategory.FilesystemWrite)
              Expect.equal writes.Length 1 "one file write for the unit's four log calls"
              let write = writes.Head
              Expect.equal write.Member "CSharp.Effects.Logging.Orders::Place" "shown at the first log call"

              let calls =
                  write.Evidence.Steps
                  |> List.filter (fun s -> s.Description.Contains " logs through ")
                  |> List.map (fun s -> s.Member, s.Location |> Option.map (fun l -> l.StartLine))

              Expect.equal
                  calls
                  [ "CSharp.Effects.Logging.Orders::Place", Some 18
                    "CSharp.Effects.Logging.Orders::Place", Some 19
                    "CSharp.Effects.Logging.Orders::Cancel", Some 22
                    "CSharp.Effects.Logging.Orders::Shutdown", Some 24 ]
                  "every log call, in source order"

              Expect.equal (logged "Logging").Length 4 "the file, the directory, the console and the clock, once each"
              Expect.equal (logged "Billing").Length 4 "and once for the other unit that logs"

              Expect.exists
                  m.ExitPoints
                  (fun x -> x.Member = "CSharp.Effects.Logging.Setup::Create" && x.Payload |> Option.exists (fun p -> p.Value = Some "logs/app.log"))
                  "configuring the logger is the application's own call, with its file"

              let entry = m.EntryPoints |> List.find (fun e -> e.HandlerMember.EndsWith "Audit::Cancelled")
              let trace = m.Traces |> List.find (fun t -> t.EntryPointId = entry.Id)

              Expect.exists
                  (trace.ExitPointIds |> List.map (fun id -> m.ExitPoints |> List.find (fun x -> x.Id = id)))
                  (fun x -> x.Category = EffectCategory.FilesystemWrite && x.Member.EndsWith "Audit::Started")
                  "a route reaching a later log call of the group has the group's effects"
          }

          test "Microsoft.Extensions.Logging calls reach the provider's sinks, once per unit" {
              let m = csharpModel.Force()

              let logged =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Unit = "CSharp.Effects:CSharp.Effects.Shipping" && x.Member.EndsWith "Parcels::Ship")

              Expect.isTrue
                  (logged |> List.forall (fun x -> x.ViaAssembly = Some "Serilog.Extensions.Logging"))
                  "through the Serilog provider the container is configured with"

              let write = logged |> List.find (fun x -> x.Category = EffectCategory.FilesystemWrite)

              let calls =
                  write.Evidence.Steps
                  |> List.filter (fun s -> s.Description.Contains " logs through ")
                  |> List.map (fun s -> s.Location |> Option.map (fun l -> l.StartLine))

              Expect.equal calls [ Some 21; Some 22 ] "both log calls, in one exit point"

              Expect.exists
                  write.Evidence.Steps
                  (fun s -> s.Description = "Microsoft.Extensions.Logging.ILogger::Log calls Serilog.Extensions.Logging.SerilogLogger::Log")
                  "the library hands the call to the logger the provider made"

              Expect.containsAll
                  (logged |> List.map (fun x -> x.Category))
                  [ EffectCategory.FilesystemWrite; EffectCategory.FilesystemRead ]
                  "the file sink's effects"

              // In ASP.NET Core the logging library is part of the shared framework, and its code is not followed.
              Expect.exists
                  m.ExitPoints
                  (fun x ->
                      x.Unit = "CSharp.Endpoints:CSharp.Endpoints.Shipping"
                      && x.Member.EndsWith "Parcels::Ship"
                      && x.Category = EffectCategory.FilesystemWrite
                      && x.ViaAssembly = Some "Serilog.Extensions.Logging")
                  "also where the logging library is part of the shared framework"
          }

          test "LoggerMessage.Define delegates and [LoggerMessage] methods are log calls, grouped with the unit's others" {
              let m = csharpModel.Force()

              let logCalls (unitId: string) =
                  let write =
                      m.ExitPoints
                      |> List.filter (fun x ->
                          x.Unit = unitId
                          && x.Category = EffectCategory.FilesystemWrite
                          && x.ViaAssembly = Some "Serilog.Extensions.Logging")

                  Expect.hasLength write 1 $"{unitId}: one exit point for the unit's log calls"

                  write.Head.Evidence.Steps
                  |> List.filter (fun s -> s.Description.Contains " logs through ")
                  |> List.map (fun s -> s.Description)

              Expect.equal
                  (logCalls "CSharp.Effects:CSharp.Effects.Labels")
                  [ "CSharp.Effects.Labels.Printer::Print logs through Microsoft.Extensions.Logging.LoggerMessage::Define"
                    "CSharp.Effects.Labels.Printer::Reprint logs through CSharp.Effects.Labels.Printer::LogReprinting" ]
                  "the delegate's invocation, and the call to the generated method; not the generated body's own call"

              Expect.isFalse
                  (m.ExitPoints |> List.exists (fun x -> x.Member.EndsWith "::LogReprinting" || x.Member.EndsWith "::.cctor" && x.Unit.EndsWith "Labels"))
                  "neither the generated method nor the Define calls are exit points"

              // In ASP.NET Core, where the logging library is part of the shared framework.
              Expect.equal
                  (logCalls "CSharp.Endpoints:CSharp.Endpoints.Shipping")
                  [ "CSharp.Endpoints.Shipping.Parcels::Ship logs through Microsoft.Extensions.Logging.LoggerExtensions::LogInformation"
                    "CSharp.Endpoints.Shipping.Parcels::Hold logs through CSharp.Endpoints.Shipping.Parcels::LogHolding" ]
                  "a [LoggerMessage] method in ASP.NET Core"
          }

          test "Entity Framework Core: saving, raw SQL, bulk deletes and the calls that run a query reach the database" {
              let m = csharpModel.Force()

              let ordering =
                  m.ExitPoints |> List.filter (fun x -> x.Unit = "CSharp.Effects:CSharp.Effects.Ordering")

              let database (memberName: string) =
                  ordering
                  |> List.tryFind (fun x -> x.Member = $"CSharp.Effects.Ordering.OrderStore::{memberName}" && x.Category = EffectCategory.Database)

              for name in [ "Add"; "AddAsync"; "Purge"; "Rename"; "Archive"; "ListAsync"; "FindAsync" ] do
                  Expect.isSome (database name) $"{name} calls the database"

              let sql (memberName: string) =
                  (database memberName).Value.Payload |> Option.bind (fun p -> p.Value)

              Expect.equal (sql "Purge") (Some "DELETE FROM Orders WHERE Id < 0") "raw SQL is the payload"
              Expect.equal (sql "Rename") (Some "UPDATE Orders SET Name = {…} WHERE Id = {…}") "an interpolated one is a template"

              // A query runs when it is enumerated: foreach, ToList, First, Count, through Where and AsNoTracking.
              for name, primitive in
                  [ "All", "System.Linq.Enumerable::ToList"
                    "Count", "System.Linq.Queryable::Count"
                    "Named", "System.Linq.Queryable::FirstOrDefault"
                    "Names", "System.Collections.Generic.IEnumerable`1::GetEnumerator"
                    "Raw", "System.Linq.Enumerable::ToList"
                    "Ids", "System.Linq.Enumerable::ToList" ] do
                  Expect.equal ((database name) |> Option.map (fun x -> x.Primitive)) (Some primitive) $"{name} runs a query"

              Expect.isTrue
                  (ordering
                   |> List.exists (fun x ->
                       x.Member = "CSharp.Effects.Ordering.OrderStore/<Stream>d__13::MoveNext"
                       && x.Primitive = "System.Collections.Generic.IAsyncEnumerable`1::GetAsyncEnumerator"))
                  "await foreach runs a query"

              Expect.isNone (database "Local") "LINQ over a list is not a query"

              Expect.equal (sql "Raw") (Some "SELECT * FROM Orders") "FromSqlRaw's SQL, where the query runs"
              Expect.equal (sql "Ids") (Some "SELECT Id FROM Orders") "SqlQueryRaw's SQL"
              Expect.equal (sql "RawAsync") (Some "SELECT * FROM Orders WHERE Id = {…}") "FromSql's, run by ToListAsync"

              // EF logs through Microsoft.Extensions.Logging, which reaches the application's Serilog sinks: that is the
              // logging configuration's effect, not EF's.
              Expect.isEmpty
                  (ordering
                   |> List.filter (fun x ->
                       x.Category = EffectCategory.FilesystemWrite
                       || x.Category = EffectCategory.FilesystemRead
                       || x.Category = EffectCategory.Environment))
                  "no sink effects from EF's own log calls"
          }

          test "a library's log of its own workings is not followed: Serilog's ForContext makes a logger, and logs nothing" {
              let m = csharpModel.Force()

              Expect.isEmpty
                  (m.ExitPoints |> List.filter (fun x -> x.Member.StartsWith "CSharp.Effects.Billing.Refunds::"))
                  "ForContext, and the log calls grouped with the unit's others"

              Expect.isFalse
                  (m.ExitPoints
                   |> List.exists (fun x ->
                       x.Evidence.Steps
                       |> List.exists (fun s ->
                           System.Text.RegularExpressions.Regex.IsMatch(
                               s.Description,
                               @"calls (Serilog\.Debugging\.SelfLog::WriteLine|NLog\.Common\.InternalLogger::(Trace|Debug|Info|Warn|Error|Fatal|Log))$"
                           ))))
                  "no route through Serilog's SelfLog or NLog's InternalLogger (turning it on, as configured, is followed)"

              // log4net's warnings and errors go to the console unless it is told to be quiet.
              Expect.exists
                  m.ExitPoints
                  (fun x -> x.ViaAssembly = Some "log4net" && x.Primitive = "System.Console::get_Error")
                  "log4net's own errors are still followed"
          }

          test "NLog and log4net log calls are grouped, once per unit and effect" {
              let m = csharpModel.Force()

              let logCalls (unitId: string) (via: string) =
                  let logged =
                      m.ExitPoints
                      |> List.filter (fun x -> x.Unit = unitId && x.ViaAssembly = Some via && x.Evidence.Steps |> List.exists (fun s -> s.Description.Contains " logs through "))

                  Expect.isNonEmpty logged $"{unitId}: the log calls reach the configured targets"

                  Expect.equal
                      (logged |> List.countBy (fun x -> x.Category) |> List.filter (fun (_, n) -> n > 1))
                      []
                      $"{unitId}: one exit point per effect"

                  logged.Head.Evidence.Steps
                  |> List.filter (fun s -> s.Description.Contains " logs through ")
                  |> List.map (fun s -> s.Location |> Option.map (fun l -> l.StartLine))

              Expect.equal (logCalls "CSharp.Effects:CSharp.Effects.Packing" "NLog") [ Some 27; Some 28 ] "NLog: both log calls"
              Expect.equal (logCalls "CSharp.Effects:CSharp.Effects.Storage" "log4net") [ Some 47; Some 48 ] "log4net: both log calls"
          }

          test "NLog targets, log4net appenders and Serilog sinks named in configuration files (XML, appsettings.json) are made" {
              let m = csharpModel.Force()

              let reaches (unitId: string) (via: string) (category: EffectCategory) =
                  m.ExitPoints
                  |> List.exists (fun x ->
                      x.Unit = unitId
                      && x.ViaAssembly = Some via
                      && x.Category = category
                      && x.Evidence.Steps |> List.exists (fun s -> s.Description.Contains " logs through "))

              Expect.isTrue (reaches "CSharp.Effects:CSharp.Effects.Packing" "NLog" EffectCategory.HttpOut) "NLog's WebService target, from nlog.config"
              Expect.isTrue (reaches "CSharp.Effects:CSharp.Effects.Storage" "log4net" EffectCategory.Network) "log4net's UdpAppender, from log4net.config"
              Expect.isTrue (reaches "CSharp.Effects:CSharp.Effects.Packing" "NLog" EffectCategory.Network) "NLog's Network target, from appsettings.json"

              Expect.exists
                  m.ExitPoints
                  (fun x ->
                      x.Unit = "CSharp.Effects:CSharp.Effects.Logging"
                      && x.Primitive = "System.IO.File::AppendAllText"
                      && x.Evidence.Steps |> List.exists (fun s -> s.Description.Contains " logs through "))
                  "Serilog's sink from appsettings.json, added by calling WriteTo.Audit"

              let configured =
                  m.Diagnostics |> List.filter (fun d -> d.Code = "configured.type") |> List.map (fun d -> d.Message)

              for file, typeName in
                  [ "nlog.config", "NLog.Targets.Wrappers.AsyncTargetWrapper"
                    "nlog.config", "NLog.Targets.WebServiceTarget"
                    "log4net.config", "log4net.Appender.UdpAppender"
                    "appsettings.json", "NLog.Targets.NetworkTarget" ] do
                  Expect.exists configured (fun d -> d.StartsWith file && d.Contains $": {typeName} is analyzed as constructed") typeName

              Expect.exists
                  configured
                  (fun d -> d.StartsWith "appsettings.json" && d.Contains ": CSharp.Wrappers.AuditSinkExtensions::Audit is analyzed as called")
                  "a Serilog sink, by the method that adds it"

              Expect.exists
                  m.Diagnostics
                  (fun d -> d.Code = "configured.unresolved" && d.Message.Contains "\"Contoso\"")
                  "a sink no method adds is a warning"

              Expect.exists
                  m.Diagnostics
                  (fun d -> d.Code = "configured.unresolved" && d.Message.Contains "Contoso.Logging.MissingAppender")
                  "a type the analysis cannot read is a warning"
          }

          test "what a configured object does when it is made happens at the call that loads the configuration" {
              let m = csharpModel.Force()

              let made =
                  m.ExitPoints
                  |> List.filter (fun x ->
                      x.Evidence.Steps |> List.exists (fun s -> s.Description.Contains " loads the configuration: "))

              let sink =
                  made
                  |> List.tryFind (fun x ->
                      x.Member = "CSharp.Effects.Loading.Setup::FromSettings"
                      && x.ViaAssembly = Some "Serilog.Sinks.File"
                      && x.Category = EffectCategory.FilesystemWrite)

              Expect.isSome sink "WriteTo.File, named in appsettings.json, makes its file where ReadFrom.Configuration is called"

              Expect.exists
                  sink.Value.Evidence.Steps
                  (fun s ->
                      s.Description.StartsWith "Serilog.ConfigurationLoggerConfigurationExtensions::Configuration loads the configuration: appsettings.json names \"File\", made by Serilog.FileLoggerConfigurationExtensions::File")
                  "the evidence names the file and what it names"

              Expect.isEmpty
                  (made |> List.filter (fun x -> not (x.Member.StartsWith "CSharp.Effects.Loading.")))
                  "only at the calls that load a configuration, not at log calls"
          }

          test "a warm summary cache keeps what configured types reach" {
              let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floorplan-cache-" + string (System.Guid.NewGuid()))
              let store = Caching.Store(Some dir)
              let solution = Fixtures.csharp.Force()

              let logged (m: AnalysisModel) =
                  m.ExitPoints
                  |> List.filter (fun x ->
                      x.ViaAssembly = Some "NLog" || x.ViaAssembly = Some "log4net" || x.Member.StartsWith "CSharp.Effects.Loading.")
                  |> List.map (fun x -> x.Category, x.Member, x.Primitive, x.ViaAssembly, x.Location)

              let cold = Pipeline.buildModel ignore store [] [] solution
              let warm = Pipeline.buildModel ignore store [] [] solution
              Expect.exists (logged warm) (fun (_, _, _, via, _) -> via = Some "Serilog.Sinks.File") "what a configured sink does when made"
              Expect.equal (logged warm) (logged cold) "same log effects"
              Expect.equal (logged cold) (logged (csharpModel.Force())) "as without a cache"
              try System.IO.Directory.Delete(dir, true) with _ -> ()
          }

          test "Entity Framework Core: a query returned from another method runs where it is enumerated" {
              let m = csharpModel.Force()

              let database (memberName: string) =
                  m.ExitPoints
                  |> List.tryFind (fun x ->
                      x.Member = $"CSharp.Effects.Reporting.OrderReports::{memberName}" && x.Category = EffectCategory.Database)

              for name in [ "All"; "Count"; "Named"; "Cancelled" ] do
                  Expect.isSome (database name) $"{name} runs a query a repository returns"

              Expect.isNone (database "Local") "a list made into a queryable is not a query"

              Expect.equal
                  ((database "Cancelled").Value.Payload |> Option.bind (fun p -> p.Value))
                  (Some "SELECT * FROM Orders WHERE Cancelled = 1")
                  "the SQL the caller passes to the repository's FromSqlRaw"

              Expect.exists
                  (database "Count").Value.Evidence.Steps
                  (fun s -> s.Description.Contains "returned by CSharp.Effects.Reporting.OrderRepository::All")
                  "through the interface, the implementation that returns the query"
          }

          test "Entity Framework Core: a query passed to a method that enumerates it runs at the call" {
              let m = csharpModel.Force()

              let database (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x ->
                      x.Member = $"CSharp.Effects.Printing.OrderPrinting::{memberName}" && x.Category = EffectCategory.Database)

              for name, primitive in
                  [ "Print", "System.Collections.Generic.IEnumerable`1::GetEnumerator"
                    "Shipped", "System.Linq.Enumerable::ToList"
                    "Again", "System.Collections.Generic.IEnumerable`1::GetEnumerator"
                    "Summarize", "System.Linq.Enumerable::ToList"
                    "Counted", "System.Collections.Generic.IEnumerable`1::GetEnumerator"
                    "PrintAsync", "System.Collections.Generic.IEnumerable`1::GetEnumerator"
                    "Later", "System.Linq.Enumerable::ToList"
                    "Listed", "System.String::Join"
                    "Names", "System.String::Concat"
                    "Added", "System.Collections.Generic.List`1::AddRange" ] do
                  Expect.equal (database name |> List.map (fun x -> x.Primitive)) [ primitive ] $"{name} runs the query it passes"

              Expect.isEmpty (database "Local") "a list passed in is not a query"

              Expect.equal
                  ((database "Shipped").Head.Payload |> Option.bind (fun p -> p.Value))
                  (Some "SELECT * FROM Orders WHERE Shipped = 1")
                  "the SQL of the query passed"

              Expect.exists
                  (database "Again").Head.Evidence.Steps
                  (fun s -> s.Description = "CSharp.Effects.Printing.Printer::Again passes it to CSharp.Effects.Printing.Printer::Print as parameter orders")
                  "through the method that passes it on"

              Expect.equal ((database "Counted").Head.ViaAssembly) (Some "CSharp.Wrappers") "enumerated in a dependency"

              Expect.exists
                  (database "PrintAsync").Head.Evidence.Steps
                  (fun s -> s.Description.Contains "passes it to CSharp.Effects.Printing.Printer/<PrintAsync>" && s.Description.EndsWith "as field orders")
                  "kept in the async method's state machine"

              // The methods that enumerate their parameter have no query of their own.
              Expect.isEmpty
                  (m.ExitPoints
                   |> List.filter (fun x -> x.Member.StartsWith "CSharp.Effects.Printing.Printer::" || x.Member.StartsWith "CSharp.Effects.Printing.Summary::"))
                  "nothing where the parameter is enumerated"
          }

          test "Entity Framework Core: a query passed to an iterator runs where the iterator is enumerated" {
              let m = csharpModel.Force()

              let database (memberName: string) =
                  m.ExitPoints
                  |> List.filter (fun x -> x.Member = memberName && x.Category = EffectCategory.Database)

              Expect.isEmpty (database "CSharp.Effects.Printing.TitlePrinting::Made") "making the iterator does not run it"

              let listed = database "CSharp.Effects.Printing.TitlePrinting::Listed"
              Expect.equal (listed |> List.map (fun x -> x.Primitive)) [ "System.Linq.Enumerable::ToList" ] "enumerating the iterator runs it"

              Expect.exists
                  listed.Head.Evidence.Steps
                  (fun s -> s.Description.Contains "enumerated by the iterator CSharp.Effects.Printing.Titles::Of")
                  "through the iterator, which is passed the query as its second argument"

              Expect.equal
                  (database "CSharp.Effects.Printing.TitlePrinting::Counted" |> List.map (fun x -> x.Primitive))
                  [ "System.Linq.Enumerable::Count" ]
                  "a method that enumerates the iterator it passes its parameter to runs the query passed"

              Expect.isEmpty
                  (m.ExitPoints |> List.filter (fun x -> x.Member.StartsWith "CSharp.Effects.Printing.Titles"))
                  "nothing in the iterator, or where it is enumerated"
          }

          test "effects in C#: every category, behind lambdas, interfaces, a container, async methods and iterators" {
              let m = csharpModel.Force()

              let has (memberPart: string) (category: EffectCategory) =
                  Expect.exists
                      m.ExitPoints
                      (fun x -> x.Member.Contains memberPart && x.Category = category && x.Unit.StartsWith "CSharp.Effects:")
                      $"{memberPart}: {category}"

              has "Direct::Fetch" EffectCategory.HttpOut
              has "Direct::ReadConfig" EffectCategory.FilesystemRead
              has "Direct::WriteReport" EffectCategory.FilesystemWrite
              has "Direct::RunGit" EffectCategory.Process
              has "Database::Sqlite" EffectCategory.Database
              has "Database::Postgres" EffectCategory.Database
              has "<SaveAll>" EffectCategory.FilesystemWrite
              has "FileStore::Save" EffectCategory.FilesystemWrite
              has "AuditStore::Save" EffectCategory.FilesystemWrite
              has "<FetchAsync>" EffectCategory.HttpOut
              has "<ReadLines>" EffectCategory.FilesystemRead
              // LibraryImport: the P/Invoke is in code a source generator adds; the call site is the user's.
              has "Hidden::Pid" EffectCategory.Native

              let via memberName =
                  m.ExitPoints |> List.filter (fun x -> x.Member = memberName) |> List.choose (fun x -> x.ViaAssembly)

              Expect.contains (via "CSharp.Effects.Wrapped::QueryNames") "Dapper" "through Dapper"
              Expect.contains (via "CSharp.Effects.Wrapped::FetchUsers") "Flurl.Http" "through Flurl"
              Expect.exists m.Diagnostics (fun d -> d.Code = "blind-spot.reflection" && d.Message.Contains "Hidden::Plugin") "reflection"
          }

          test "SQL text of ADO.NET commands: set as a property, in an object initializer, as a template, or unknown" {
              let m = csharpModel.Force()

              let sql memberName =
                  m.ExitPoints
                  |> List.find (fun x -> x.Member = memberName && x.Primitive.Contains "DbCommand::")
                  |> fun x -> x.Payload |> Option.map (fun p -> p.Role, p.Kind, p.Value)

              Expect.equal (sql "CSharp.Effects.Database::Sqlite") (Some("sql", PayloadKind.Literal, Some "DELETE FROM sessions")) "property"
              Expect.equal (sql "CSharp.Effects.Database::Postgres") (Some("sql", PayloadKind.Literal, Some "SELECT count(*) FROM orders")) "object initializer"
              Expect.equal (sql "CSharp.Effects.Database::Purge") (Some("sql", PayloadKind.Template, Some "DELETE FROM {…}")) "interpolated"
              Expect.equal (sql "CSharp.Effects.Database::Run") (Some("sql", PayloadKind.Dynamic, None)) "a command made elsewhere"
          }

          test "C#: source generators run, so code that calls what they generate compiles" {
              let m = csharpModel.Force()
              Expect.isFalse (m.Diagnostics |> List.exists (fun d -> d.Code.StartsWith "structure.")) "no compile errors"
          }

          test "minimal APIs: one entry per route; cached delegates are not taken for library responses" {
              let m = csharpModel.Force()
              let endpoints = entries m "CSharp.Endpoints"

              Expect.isFalse (endpoints |> List.exists (fun e -> e.HandlerMember.Contains "library function")) "no library responses"

              // A hub's methods share its route: the invocation's target chooses among them.
              let keys =
                  endpoints
                  |> List.filter (fun e -> not (e.HandlerMember.StartsWith "CSharp.Endpoints.Chat."))
                  |> List.map (fun e -> e.HttpMethod, e.Route)

              Expect.equal keys (List.distinct keys) "one entry per method and route"

              let byRoute route =
                  endpoints |> List.find (fun e -> e.Route = Some route && e.HttpMethod <> Some "DELETE")

              Expect.stringEnds (byRoute "/api/orders").HandlerMember "Api::ListOrders" "method group"
              Expect.stringEnds (byRoute "/api/orders/{id:int}").HandlerMember "Api::GetOrder" "a lambda that calls a method"
              Expect.stringContains (byRoute "/admin/users").HandlerMember "Admin/<>c" "a lambda is its own handler"
              Expect.equal (byRoute "/admin/reports/rebuild").HttpMethod (Some "POST") "nested group behind a convention chain"
          }

          test "hand-written routing, switch expressions and tables in C# give the F# results" {
              let m = csharpModel.Force()

              let routes app =
                  entries m app
                  |> List.choose (fun e ->
                      match e.Completeness with
                      | Completeness.Complete -> Some(defaultArg e.HttpMethod "-" + " " + defaultArg e.Route "-")
                      | _ -> None)
                  |> set

              for r in [ "GET /health"; "OPTIONS /orders"; "GET /orders/{id}"; "PUT /orders/{id}"; "DELETE /orders/{id}" ] do
                  Expect.contains (routes "CSharp.Middleware") r $"middleware {r}"

              // Seven string cases (Roslyn compares hashes first, then the strings), then list patterns.
              Expect.equal (routes "CSharp.Switch").Count 9 "every case of both switches"
              Expect.contains (routes "CSharp.Switch") "POST /api/orders" "method and path from the tuple"
              Expect.contains (routes "CSharp.Switch") "DELETE /api/orders/{id}" "a list pattern with a parameter"

              let table = entries m "CSharp.Table" |> List.map (fun e -> e.Route) |> List.sort
              Expect.equal table [ Some "/health"; Some "/orders" ] "dictionary initializer keys"
          }

          test "routing code in a C# dependency reaches handlers the application passes as method groups" {
              let m = csharpModel.Force()

              let complete =
                  entries m "CSharp.Delegated"
                  |> List.filter (fun e -> e.Completeness = Completeness.Complete)
                  |> List.map (fun e -> defaultArg e.HttpMethod "-" + " " + defaultArg e.Route "-", e.HandlerMember)
                  |> List.sort

              Expect.equal
                  complete
                  [ "DELETE /orders/{id}", "CSharp.Delegated.Orders::Remove"
                    "GET /health", "CSharp.Delegated.Orders::Health"
                    "GET /orders/{id}", "CSharp.Delegated.Orders::Show" ]
                  "routes from the library, handlers from the application, parameters named by the handlers"
          }

          test "MVC controllers in C#: attribute routes and the default conventional route" {
              let m = csharpModel.Force()

              let routes =
                  entries m "CSharp.Controllers"
                  // The application's Razor Pages have a test of their own.
                  |> List.filter (fun e -> not (e.HandlerMember.Contains ".Pages.Shop." || e.HandlerMember.StartsWith "AspNetCoreGeneratedDocument."))
                  |> List.map (fun e -> defaultArg e.HttpMethod "*", defaultArg e.Route "-")
                  |> List.sort

              Expect.equal
                  routes
                  [ "*", "/"; "*", "/Home"; "*", "/Home/Index/{id?}"; "*", "/Pages/About/{id?}"; "*", "/Pages/Describe/{id?}"; "*", "/Users"
                    "GET", "/api/Orders"; "GET", "/api/Orders/{id:int}"
                    "POST", "/api/Orders" ]
                  "routes (GitHub issue 3: an override of Controller.OnActionExecuting is not an action)"
          }

          test "a conventional route alone also maps attribute routes, and does not reach them" {
              let m = csharpModel.Force()

              let routes =
                  entries m "CSharp.Conventional"
                  |> List.map (fun e -> defaultArg e.HttpMethod "*", defaultArg e.Route "-", e.HandlerMember.Split("::")[1])
                  |> List.sort

              // As the application serves them (checked with requests): /Route/Main is a 404.
              Expect.equal
                  routes
                  [ "*", "/", "Main"
                    "*", "/Apa", "A01"
                    "*", "/Bepa", "A02"
                    "*", "/Home/Index/{id?}", "Index"
                    "GET", "/r/Derpa", "A15"
                    "POST", "/Herpa", "A04" ]
                  "routes"
          }

          test "Razor components and pages are units of the namespaces their generated code declares" {
              let m = csharpModel.Force()
              let pages = m.Units |> List.find (fun u -> u.Id = "CSharp.Blazor:CSharp.Blazor.Components.Pages")

              Expect.equal pages.Types [ "Badge"; "Counter"; "Home"; "Order" ] "the components of the folder, from their .razor files"
              Expect.stringEnds pages.Location.Value.File ".razor" "located in a Razor file, not in generated code"

              Expect.isTrue
                  (m.EntryPoints
                   |> List.filter (fun e -> e.ApplicationId = "CSharp.Blazor" && e.Category = EffectCategory.HttpIn)
                   |> List.forall (fun e -> e.HandlerUnit = pages.Id))
                  "the routable components' handlers are in it, not in the global namespace"

              let audit = m.EntryPoints |> List.find (fun e -> e.Route = Some "/Admin/Audit")
              Expect.equal audit.HandlerUnit "CSharp.Controllers:AspNetCoreGeneratedDocument" "a Razor page without a model, in the namespace it is generated in"

              Expect.isFalse
                  (m.Dependencies |> List.exists (fun d -> d.FromUnit = "CSharp.Controllers:" && d.ToUnit = "CSharp.Controllers:AspNetCoreGeneratedDocument"))
                  "the assembly attributes that list the generated pages are no dependency"

              Expect.isTrue
                  (m.TypeDependencies
                   |> List.exists (fun d -> d.FromType = "AspNetCoreGeneratedDocument.Pages_Shop_Item" && d.ToType = "CSharp.Controllers.Pages.Shop.ItemModel"))
                  "a page uses its model"
          }

          test "ViewBag in controllers and Razor views is not a blind spot; other dynamic values are" {
              let m = csharpModel.Force()

              let dynamicIn (memberPrefix: string) =
                  m.Diagnostics
                  |> List.filter (fun d -> d.Code = "blind-spot.dynamic" && d.Message.StartsWith memberPrefix)
                  |> List.length

              // GitHub issue 2: ViewBag is the view data dictionary, and @ViewBag.Title in a view writes it out.
              Expect.equal (dynamicIn "CSharp.Controllers.PagesController::About") 0 "ViewBag set in an action"
              Expect.equal (dynamicIn "AspNetCoreGeneratedDocument.Views_Pages_About") 0 "ViewBag set and written in a view"
              // value.Name, and its conversion to string.
              Expect.equal (dynamicIn "CSharp.Controllers.PagesController::Describe") 2 "a dynamic parameter"
          }

          test "recognizer: Roslyn's delegate caches start out null" {
              Expect.isTrue (CSharpCodegen.isLazyCache (field "Program/<>c" "<>9__0_1")) "cached lambda"
              Expect.isTrue (CSharpCodegen.isLazyCache (field "App.Routes/<>O" "<0>__List")) "cached method group"
              Expect.isFalse (CSharpCodegen.isLazyCache (field "Program/<>c" "<>9")) "the singleton is set by the static constructor"
              Expect.isFalse (CSharpCodegen.isLazyCache (field "App.Routes" "Table")) "an ordinary static field"
          }

          test "payloads built as C# writes them: interpolated strings and composite format strings" {
              let m = csharpModel.Force()

              let payload memberName category =
                  m.ExitPoints
                  |> List.find (fun x -> x.Member = memberName && x.Category = category)
                  |> fun x -> x.Payload |> Option.bind (fun p -> p.Value)

              Expect.equal (payload "CSharp.Endpoints.Api::GetOrder" EffectCategory.FilesystemRead) (Some "orders/{…}.json") "interpolated"
              Expect.equal (payload "CSharp.Endpoints.Api::CreateOrder" EffectCategory.FilesystemWrite) (Some "orders/{…}.json") "string.Format"
          }

          test "the site shows C# lambdas by their enclosing method, not their compiler-generated names" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floorplan-tests", System.Guid.NewGuid().ToString "N")
                  let index = Floorplan.Rendering.WebsiteRenderer.render (csharpModel.Force()) { Path = dir; SingleFile = false }
                  let page = Browser.dumpDom browser index "#/entry-points"

                  Expect.stringContains page "Admin › lambda in Map" "lambda in a method"
                  Expect.stringContains page "Program › lambda in top-level statements" "lambda in top-level statements"
                  // The compiler's name stays in the tooltip only.
                  Expect.isFalse (page.Contains ">CSharp.Endpoints.Admin/&lt;&gt;c") "no compiler-generated name shown"
                  Expect.isFalse (page.Contains "title=\"\"></a>") "the global namespace links by its label"
                  Expect.stringContains page "(global namespace)</a>" "global namespace label"

                  let unitPage = Browser.dumpDom browser index "#/unit/CSharp.Effects%3ACSharp.Effects"
                  Expect.stringContains unitPage "Types (" "a namespace lists its types"
                  Expect.stringContains unitPage "AuditStore, Database, Direct, FileStore" "sorted type names"
          } ]
