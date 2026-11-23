workspace "CSharp" "CSharp, derived by Floorplan from its code" {
    model {
        !impliedRelationships "com.structurizr.model.CreateImpliedRelationshipsUnlessSameRelationshipExistsStrategy"
        system = softwareSystem "CSharp" "The CSharp solution: 18 applications in 18 projects" {
            container_CSharp_Blazor = container "CSharp.Blazor" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Blazor_CSharp_Blazor_ = component "(global namespace)" "A C# namespace of CSharp.Blazor" "C# namespace"
                component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components = component "CSharp.Blazor.Components" "A C# namespace of CSharp.Blazor" "C# namespace"
                component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components_Pages = component "CSharp.Blazor.Components.Pages" "A C# namespace of CSharp.Blazor" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_Clients = container "CSharp.Clients" "A message consumer" "C# · net8.0" "message consumer" {
                component_CSharp_Clients_CSharp_Clients_ = component "(global namespace)" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching = component "CSharp.Clients.Caching" "A C# namespace of CSharp.Clients" "C# namespace" "handles requests,reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Catalog = component "CSharp.Clients.Catalog" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Chat = component "CSharp.Clients.Chat" "A C# namespace of CSharp.Clients" "C# namespace" "handles requests,reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Documents = component "CSharp.Clients.Documents" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Files = component "CSharp.Clients.Files" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_GitHub = component "CSharp.Clients.GitHub" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
                component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Notifications = component "CSharp.Clients.Notifications" "A C# namespace of CSharp.Clients" "C# namespace" "reaches out"
            }
            container_CSharp_Cloud = container "CSharp.Cloud" "A message consumer" "C# · net8.0" "message consumer" {
                component_CSharp_Cloud_CSharp_Cloud_ = component "(global namespace)" "A C# namespace of CSharp.Cloud" "C# namespace" "reaches out"
                component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Catalog = component "CSharp.Cloud.Catalog" "A C# namespace of CSharp.Cloud" "C# namespace" "reaches out"
                component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs = component "CSharp.Cloud.Jobs" "A C# namespace of CSharp.Cloud" "C# namespace" "handles requests,reaches out"
                component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Orders = component "CSharp.Cloud.Orders" "A C# namespace of CSharp.Cloud" "C# namespace" "reaches out"
                component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Reports = component "CSharp.Cloud.Reports" "A C# namespace of CSharp.Cloud" "C# namespace" "reaches out"
                component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Search = component "CSharp.Cloud.Search" "A C# namespace of CSharp.Cloud" "C# namespace" "reaches out"
            }
            container_CSharp_Controllers = container "CSharp.Controllers" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Controllers_CSharp_Controllers_ = component "(global namespace)" "A C# namespace of CSharp.Controllers" "C# namespace"
                component_CSharp_Controllers_CSharp_Controllers_Areas_Admin_Pages = component "Areas/Admin/Pages" "A C# folder of CSharp.Controllers" "C# folder" "handles requests"
                component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers = component "CSharp.Controllers" "A C# namespace of CSharp.Controllers" "C# namespace" "handles requests"
                component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Admin = component "CSharp.Controllers.Admin" "A C# namespace of CSharp.Controllers" "C# namespace" "handles requests"
                component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Pages_Shop = component "CSharp.Controllers.Pages.Shop" "A C# namespace of CSharp.Controllers" "C# namespace" "handles requests,reaches out"
                component_CSharp_Controllers_CSharp_Controllers_Pages_Shop = component "Pages/Shop" "A C# folder of CSharp.Controllers" "C# folder"
                component_CSharp_Controllers_CSharp_Controllers_Views_Pages = component "Views/Pages" "A C# folder of CSharp.Controllers" "C# folder"
            }
            container_CSharp_Conventional = container "CSharp.Conventional" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Conventional_CSharp_Conventional_ = component "(global namespace)" "A C# namespace of CSharp.Conventional" "C# namespace"
                component_CSharp_Conventional_CSharp_Conventional_CSharp_Conventional_Controllers = component "CSharp.Conventional.Controllers" "A C# namespace of CSharp.Conventional" "C# namespace" "handles requests"
            }
            container_CSharp_Dapr = container "CSharp.Dapr" "A web application and message consumer" "C# · net8.0" "web application,message consumer" {
                component_CSharp_Dapr_CSharp_Dapr_ = component "(global namespace)" "A C# namespace of CSharp.Dapr" "C# namespace" "handles requests"
                component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout = component "CSharp.Dapr.Checkout" "A C# namespace of CSharp.Dapr" "C# namespace" "handles requests,reaches out"
                component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts = component "CSharp.Dapr.Payouts" "A C# namespace of CSharp.Dapr" "C# namespace" "handles requests,reaches out"
                component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts = component "CSharp.Dapr.Receipts" "A C# namespace of CSharp.Dapr" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_Delegated = container "CSharp.Delegated" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Delegated_CSharp_Delegated_ = component "(global namespace)" "A C# namespace of CSharp.Delegated" "C# namespace"
                component_CSharp_Delegated_CSharp_Delegated_CSharp_Delegated = component "CSharp.Delegated" "A C# namespace of CSharp.Delegated" "C# namespace" "handles requests"
            }
            container_CSharp_Effects = container "CSharp.Effects" "A background worker" "C# · net8.0" "background worker" {
                component_CSharp_Effects_CSharp_Effects_ = component "(global namespace)" "A C# namespace of CSharp.Effects" "C# namespace"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects = component "CSharp.Effects" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Billing = component "CSharp.Effects.Billing" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Labels = component "CSharp.Effects.Labels" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading = component "CSharp.Effects.Loading" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Logging = component "CSharp.Effects.Logging" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering = component "CSharp.Effects.Ordering" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Packing = component "CSharp.Effects.Packing" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Printing = component "CSharp.Effects.Printing" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Reporting = component "CSharp.Effects.Reporting" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Shipping = component "CSharp.Effects.Shipping" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
                component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage = component "CSharp.Effects.Storage" "A C# namespace of CSharp.Effects" "C# namespace" "reaches out"
            }
            container_CSharp_Endpoints = container "CSharp.Endpoints" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Endpoints_CSharp_Endpoints_ = component "(global namespace)" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints = component "CSharp.Endpoints" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests,reaches out"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing = component "CSharp.Endpoints.Auditing" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests,reaches out"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Chat = component "CSharp.Endpoints.Chat" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests,reaches out"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Configured = component "CSharp.Endpoints.Configured" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered = component "CSharp.Endpoints.Filtered" "A C# namespace of CSharp.Endpoints" "C# namespace" "handles requests,reaches out"
                component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Shipping = component "CSharp.Endpoints.Shipping" "A C# namespace of CSharp.Endpoints" "C# namespace" "reaches out"
            }
            container_CSharp_Functions = container "CSharp.Functions" "A web application, message consumer and background worker" "C# · net8.0" "web application,message consumer,background worker" {
                component_CSharp_Functions_CSharp_Functions_ = component "(global namespace)" "A C# namespace of CSharp.Functions" "C# namespace"
                component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Maintenance = component "CSharp.Functions.Maintenance" "A C# namespace of CSharp.Functions" "C# namespace" "reaches out"
                component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Orders = component "CSharp.Functions.Orders" "A C# namespace of CSharp.Functions" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_FunctionsInProcess = container "CSharp.FunctionsInProcess" "A web application, message consumer and background worker" "C# · net8.0" "web application,message consumer,background worker" {
                component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Maintenance = component "CSharp.FunctionsInProcess.Maintenance" "A C# namespace of CSharp.FunctionsInProcess" "C# namespace" "reaches out"
                component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Orders = component "CSharp.FunctionsInProcess.Orders" "A C# namespace of CSharp.FunctionsInProcess" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_Grpc = container "CSharp.Grpc" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Grpc_CSharp_Grpc_ = component "(global namespace)" "A C# namespace of CSharp.Grpc" "C# namespace" "handles requests"
                component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory = component "CSharp.Grpc.Inventory" "A C# namespace of CSharp.Grpc" "C# namespace" "handles requests,reaches out"
                component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Restocking = component "CSharp.Grpc.Restocking" "A C# namespace of CSharp.Grpc" "C# namespace" "reaches out"
            }
            container_CSharp_Messaging = container "CSharp.Messaging" "A message consumer" "C# · net8.0" "message consumer" {
                component_CSharp_Messaging_CSharp_Messaging_ = component "(global namespace)" "A C# namespace of CSharp.Messaging" "C# namespace"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Configured = component "CSharp.Messaging.Configured" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders = component "CSharp.Messaging.Orders" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Payments = component "CSharp.Messaging.Payments" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing = component "CSharp.Messaging.Publishing" "A C# namespace of CSharp.Messaging" "C# namespace" "reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds = component "CSharp.Messaging.Refunds" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales = component "CSharp.Messaging.Sales" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
                component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping = component "CSharp.Messaging.Shipping" "A C# namespace of CSharp.Messaging" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_Middleware = container "CSharp.Middleware" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Middleware_CSharp_Middleware_ = component "(global namespace)" "A C# namespace of CSharp.Middleware" "C# namespace"
                component_CSharp_Middleware_CSharp_Middleware_CSharp_Middleware = component "CSharp.Middleware" "A C# namespace of CSharp.Middleware" "C# namespace" "handles requests"
            }
            container_CSharp_Orleans = container "CSharp.Orleans" "A web application, message consumer and background worker" "C# · net8.0" "web application,message consumer,background worker" {
                component_CSharp_Orleans_CSharp_Orleans_ = component "(global namespace)" "A C# namespace of CSharp.Orleans" "C# namespace" "handles requests,reaches out"
                component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders = component "CSharp.Silo.Orders" "A C# namespace of CSharp.Orleans" "C# namespace" "handles requests,reaches out"
                component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Stock = component "CSharp.Silo.Stock" "A C# namespace of CSharp.Orleans" "C# namespace" "handles requests,reaches out"
            }
            container_CSharp_Switch = container "CSharp.Switch" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Switch_CSharp_Switch_ = component "(global namespace)" "A C# namespace of CSharp.Switch" "C# namespace"
                component_CSharp_Switch_CSharp_Switch_CSharp_Switch = component "CSharp.Switch" "A C# namespace of CSharp.Switch" "C# namespace" "handles requests"
            }
            container_CSharp_Table = container "CSharp.Table" "A web application" "C# · net8.0" "web application" {
                component_CSharp_Table_CSharp_Table_ = component "(global namespace)" "A C# namespace of CSharp.Table" "C# namespace"
                component_CSharp_Table_CSharp_Table_CSharp_Table = component "CSharp.Table" "A C# namespace of CSharp.Table" "C# namespace" "handles requests"
            }
            container_CSharp_Worker = container "CSharp.Worker" "A file watcher and background worker" "C# · net8.0" "file watcher,background worker" {
                component_CSharp_Worker_CSharp_Worker_ = component "(global namespace)" "A C# namespace of CSharp.Worker" "C# namespace"
                component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Cleanup = component "CSharp.Worker.Cleanup" "A C# namespace of CSharp.Worker" "C# namespace" "reaches out"
                component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports = component "CSharp.Worker.Imports" "A C# namespace of CSharp.Worker" "C# namespace" "handles requests,reaches out"
                component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Monitoring = component "CSharp.Worker.Monitoring" "A C# namespace of CSharp.Worker" "C# namespace" "reaches out"
                component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Queues = component "CSharp.Worker.Queues" "A C# namespace of CSharp.Worker" "C# namespace" "reaches out"
                component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Reporting = component "CSharp.Worker.Reporting" "A C# namespace of CSharp.Worker" "C# namespace" "reaches out"
            }
        }
        external_database = softwareSystem "Database" "A database reached through the platform's data access" "External"
        external_filesystem = softwareSystem "File system" "Files and directories" "External"
        external_http = softwareSystem "HTTP services" "HTTP services whose address is not known" "External"
        external_http_api_example_org = softwareSystem "api.example.org" "An HTTP service" "External"
        external_http_api_github_com = softwareSystem "api.github.com" "An HTTP service" "External"
        external_http_billing = softwareSystem "billing" "An HTTP service" "External"
        external_http_inventory = softwareSystem "inventory" "An HTTP service" "External"
        external_http_payments_example_org = softwareSystem "payments.example.org" "An HTTP service" "External"
        external_http_pricing = softwareSystem "pricing" "An HTTP service" "External"
        external_http_shipping = softwareSystem "shipping" "An HTTP service" "External"
        external_http_status_example_org = softwareSystem "status.example.org" "An HTTP service" "External"
        external_library_Amazon_DynamoDBv2 = softwareSystem "Amazon.DynamoDBv2" "A database reached through its client library" "External"
        external_library_Amazon_S3 = softwareSystem "Amazon.S3" "A service reached through its client library" "External"
        external_library_Amazon_SQS = softwareSystem "Amazon.SQS" "A service reached through its client library" "External"
        external_library_Azure_Messaging_ServiceBus = softwareSystem "Azure.Messaging.ServiceBus" "A service reached through its client library" "External"
        external_library_Azure_Storage_Blobs = softwareSystem "Azure.Storage.Blobs" "A service reached through its client library" "External"
        external_library_Confluent_Kafka = softwareSystem "Confluent.Kafka" "A service reached through its client library" "External"
        external_library_Dapr = softwareSystem "Dapr" "A service reached through its client library" "External"
        external_library_Elastic_Clients_Elasticsearch = softwareSystem "Elastic.Clients.Elasticsearch" "A database reached through its client library" "External"
        external_library_Grpc_Core = softwareSystem "Grpc.Core" "A service reached through its client library" "External"
        external_library_MailKit = softwareSystem "MailKit" "A service reached through its client library" "External"
        external_library_MassTransit = softwareSystem "MassTransit" "A service reached through its client library" "External"
        external_library_Microsoft_AspNetCore_SignalR_Client = softwareSystem "Microsoft.AspNetCore.SignalR.Client" "A service reached through its client library" "External"
        external_library_Microsoft_Azure_Cosmos = softwareSystem "Microsoft.Azure.Cosmos" "A database reached through its client library" "External"
        external_library_Microsoft_Azure_Functions_Worker = softwareSystem "Microsoft.Azure.Functions.Worker" "A service reached through its client library" "External"
        external_library_Microsoft_Azure_WebJobs = softwareSystem "Microsoft.Azure.WebJobs" "A service reached through its client library" "External"
        external_library_Microsoft_EntityFrameworkCore = softwareSystem "Microsoft.EntityFrameworkCore" "A database reached through its client library" "External"
        external_library_MongoDB_Driver = softwareSystem "MongoDB.Driver" "A database reached through its client library" "External"
        external_library_NServiceBus = softwareSystem "NServiceBus" "A service reached through its client library" "External"
        external_library_Orleans = softwareSystem "Orleans" "A service reached through its client library" "External"
        external_library_RabbitMQ_Client = softwareSystem "RabbitMQ.Client" "A service reached through its client library" "External"
        external_library_SendGrid = softwareSystem "SendGrid" "A service reached through its client library" "External"
        external_library_StackExchange_Redis = softwareSystem "StackExchange.Redis" "A service reached through its client library" "External"
        external_native = softwareSystem "Native libraries" "Code outside .NET, called through P/Invoke" "External"
        external_network = softwareSystem "Network" "Hosts reached over the network" "External"
        external_processes = softwareSystem "Operating system" "Processes the application starts or inspects" "External"
        person_user = person "User" "A person or client calling the HTTP endpoints"

        component_CSharp_Blazor_CSharp_Blazor_ -> component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components "uses"
        component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components -> component_CSharp_Blazor_CSharp_Blazor_ "uses"
        component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components_Pages -> external_filesystem "writes files"
        component_CSharp_Clients_CSharp_Clients_ -> component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching "uses"
        component_CSharp_Clients_CSharp_Clients_ -> component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Documents "uses"
        component_CSharp_Clients_CSharp_Clients_ -> component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Files "uses"
        component_CSharp_Clients_CSharp_Clients_ -> external_filesystem "reads files" "MongoDB.Driver"
        component_CSharp_Clients_CSharp_Clients_ -> external_library_StackExchange_Redis "sends and receives over the network"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching -> external_filesystem "writes files"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching -> external_library_StackExchange_Redis "reads and writes data"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching -> external_library_StackExchange_Redis "sends and receives over the network"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching -> external_network "sends and receives over the network" "StackExchange.Redis"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Catalog -> external_http "sends HTTP requests" "HTTP"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Chat -> external_filesystem "writes files"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Chat -> external_library_Microsoft_AspNetCore_SignalR_Client "sends and receives over the network"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Documents -> external_library_MongoDB_Driver "reads and writes data"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Files -> external_filesystem "reads files"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Files -> external_library_Azure_Storage_Blobs "sends HTTP requests"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_GitHub -> external_http_api_github_com "sends HTTP requests" "HTTP"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_GitHub -> external_http_status_example_org "sends HTTP requests" "HTTP"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Notifications -> external_library_MailKit "sends and receives over the network"
        component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Notifications -> external_library_SendGrid "sends HTTP requests"
        component_CSharp_Cloud_CSharp_Cloud_ -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Catalog "uses"
        component_CSharp_Cloud_CSharp_Cloud_ -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs "uses"
        component_CSharp_Cloud_CSharp_Cloud_ -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Orders "uses"
        component_CSharp_Cloud_CSharp_Cloud_ -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Reports "uses"
        component_CSharp_Cloud_CSharp_Cloud_ -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Search "uses"
        component_CSharp_Cloud_CSharp_Cloud_ -> external_filesystem "reads files" "AWSSDK.DynamoDBv2, AWSSDK.S3, AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_ -> external_filesystem "writes files" "AWSSDK.DynamoDBv2, AWSSDK.S3, AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_ -> external_processes "starts or inspects processes" "Microsoft.Azure.Cosmos.Client"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Catalog -> external_library_Microsoft_Azure_Cosmos "reads and writes data"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs -> external_filesystem "reads files" "AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs -> external_filesystem "writes files" "AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs -> external_http "sends HTTP requests" "AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs -> external_library_Amazon_SQS "sends and receives over the network"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs -> external_network "sends and receives over the network" "AWSSDK.SQS"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Orders -> external_library_Amazon_DynamoDBv2 "reads and writes data"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Reports -> external_library_Amazon_S3 "sends HTTP requests"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Search -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Catalog "uses"
        component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Search -> external_library_Elastic_Clients_Elasticsearch "reads and writes data"
        component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Pages_Shop -> external_filesystem "writes files"
        component_CSharp_Controllers_CSharp_Controllers_Pages_Shop -> component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Pages_Shop "uses"
        component_CSharp_Dapr_CSharp_Dapr_ -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout "uses"
        component_CSharp_Dapr_CSharp_Dapr_ -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts "uses"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts "uses"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> external_http_billing "sends HTTP requests" "HTTP"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> external_http_inventory "sends HTTP requests" "Dapr"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> external_http_pricing "sends HTTP requests" "Dapr"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> external_http_shipping "sends HTTP requests" "Dapr"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout -> external_library_Dapr "sends and receives over the network"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts -> external_library_Dapr "reads and writes data"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts "uses"
        component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts -> external_filesystem "writes files"
        component_CSharp_Delegated_CSharp_Delegated_ -> component_CSharp_Delegated_CSharp_Delegated_CSharp_Delegated "uses"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_database "reads and writes data" "CSharp.Wrappers, Dapper"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_filesystem "reads files"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_filesystem "writes files" "CSharp.Wrappers"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_http "sends HTTP requests" "Flurl.Http"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_http_api_example_org "sends HTTP requests" "CSharp.Wrappers, HTTP"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_native "calls native code"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects -> external_processes "starts or inspects processes"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Billing -> external_filesystem "reads files" "Serilog"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Billing -> external_filesystem "writes files" "Serilog"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Labels -> external_filesystem "reads files" "Serilog.Extensions.Logging"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Labels -> external_filesystem "writes files" "Serilog.Extensions.Logging"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading -> external_filesystem "reads files" "NLog.Extensions.Logging, Serilog.Settings.Configuration, log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading -> external_filesystem "writes files" "NLog.Extensions.Logging, Serilog.Sinks.File, log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading -> external_http "sends HTTP requests" "log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading -> external_processes "starts or inspects processes" "NLog.Extensions.Logging"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Logging -> external_filesystem "reads files" "Serilog, Serilog.Sinks.File"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Logging -> external_filesystem "writes files" "Serilog, Serilog.Sinks.File"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering -> external_database "reads and writes data"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering -> external_library_Microsoft_EntityFrameworkCore "reads and writes data"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Packing -> external_filesystem "reads files" "NLog"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Packing -> external_processes "starts or inspects processes" "NLog"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Printing -> component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering "uses"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Printing -> external_database "reads and writes data" "CSharp.Wrappers"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Reporting -> component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering "uses"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Reporting -> external_database "reads and writes data"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Shipping -> external_filesystem "reads files" "Serilog.Extensions.Logging, Serilog.Sinks.File"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Shipping -> external_filesystem "writes files" "Serilog.Extensions.Logging, Serilog.Sinks.File"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage -> external_filesystem "reads files" "log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage -> external_filesystem "writes files" "log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage -> external_http "sends HTTP requests" "log4net"
        component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage -> external_network "sends and receives over the network" "log4net"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Chat "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Configured "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_ -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Shipping "uses"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints -> external_filesystem "reads files"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints -> external_filesystem "writes files"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing -> external_filesystem "reads files" "Serilog, Serilog.Sinks.File"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing -> external_filesystem "writes files" "Serilog, Serilog.Sinks.File"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Chat -> external_filesystem "writes files"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered -> external_filesystem "reads files"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered -> external_filesystem "writes files"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Shipping -> external_filesystem "reads files" "Serilog.Extensions.Logging, Serilog.Sinks.File"
        component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Shipping -> external_filesystem "writes files" "Serilog.Extensions.Logging, Serilog.Sinks.File"
        component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Maintenance -> external_filesystem "writes files"
        component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Orders -> external_filesystem "writes files"
        component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Maintenance -> external_filesystem "writes files"
        component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Orders -> external_filesystem "writes files"
        component_CSharp_Grpc_CSharp_Grpc_ -> component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory "uses"
        component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory -> external_filesystem "writes files"
        component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Restocking -> component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory "uses"
        component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Restocking -> external_library_Grpc_Core "sends HTTP requests"
        component_CSharp_Messaging_CSharp_Messaging_ -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders "uses"
        component_CSharp_Messaging_CSharp_Messaging_ -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Payments "uses"
        component_CSharp_Messaging_CSharp_Messaging_ -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping "uses"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Configured -> external_filesystem "writes files"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Configured -> external_library_RabbitMQ_Client "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders -> external_filesystem "writes files"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders -> external_network "sends and receives over the network" "RabbitMQ.Client"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Payments -> external_filesystem "writes files"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping "uses"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing -> external_library_Azure_Messaging_ServiceBus "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing -> external_library_Confluent_Kafka "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing -> external_library_MassTransit "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing -> external_library_RabbitMQ_Client "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds -> external_filesystem "writes files"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds -> external_native "calls native code" "Confluent.Kafka"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales -> external_filesystem "reads files" "NServiceBus.Core"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales -> external_filesystem "writes files" "NServiceBus.Core"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales -> external_library_NServiceBus "sends and receives over the network"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping -> external_filesystem "writes files"
        component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping -> external_processes "starts or inspects processes" "MassTransit"
        component_CSharp_Middleware_CSharp_Middleware_ -> component_CSharp_Middleware_CSharp_Middleware_CSharp_Middleware "uses"
        component_CSharp_Orleans_CSharp_Orleans_ -> component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders "uses"
        component_CSharp_Orleans_CSharp_Orleans_ -> external_library_Orleans "sends and receives over the network"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders -> component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Stock "uses"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders -> external_filesystem "writes files"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders -> external_http_payments_example_org "sends HTTP requests" "HTTP"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders -> external_library_Orleans "reads and writes data"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders -> external_library_Orleans "sends and receives over the network"
        component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Stock -> external_filesystem "writes files"
        component_CSharp_Switch_CSharp_Switch_ -> component_CSharp_Switch_CSharp_Switch_CSharp_Switch "uses"
        component_CSharp_Table_CSharp_Table_ -> component_CSharp_Table_CSharp_Table_CSharp_Table "uses"
        component_CSharp_Worker_CSharp_Worker_ -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Cleanup "uses"
        component_CSharp_Worker_CSharp_Worker_ -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports "uses"
        component_CSharp_Worker_CSharp_Worker_ -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Monitoring "uses"
        component_CSharp_Worker_CSharp_Worker_ -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Queues "uses"
        component_CSharp_Worker_CSharp_Worker_ -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Reporting "uses"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Cleanup -> external_filesystem "reads files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Cleanup -> external_filesystem "writes files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports -> external_filesystem "reads files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports -> external_filesystem "writes files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Monitoring -> external_filesystem "writes files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Monitoring -> external_http "sends HTTP requests" "HTTP"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Queues -> external_filesystem "writes files"
        component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Reporting -> external_filesystem "writes files"
        external_filesystem -> component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports "notifies of changed files"
        external_library_Amazon_SQS -> component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs "delivers messages to"
        external_library_Azure_Messaging_ServiceBus -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Payments "delivers messages to"
        external_library_Azure_Messaging_ServiceBus -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds "delivers messages to"
        external_library_Confluent_Kafka -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds "delivers messages to"
        external_library_Dapr -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts "delivers messages to"
        external_library_Dapr -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts "delivers messages to"
        external_library_MassTransit -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping "delivers messages to"
        external_library_Microsoft_AspNetCore_SignalR_Client -> component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Chat "delivers messages to"
        external_library_Microsoft_Azure_Functions_Worker -> component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Orders "delivers messages to"
        external_library_Microsoft_Azure_WebJobs -> component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Orders "delivers messages to"
        external_library_NServiceBus -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales "delivers messages to"
        external_library_Orleans -> component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders "delivers messages to"
        external_library_Orleans -> component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Stock "delivers messages to"
        external_library_RabbitMQ_Client -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Configured "delivers messages to"
        external_library_RabbitMQ_Client -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders "delivers messages to"
        external_library_RabbitMQ_Client -> component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds "delivers messages to"
        external_library_StackExchange_Redis -> component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching "delivers messages to"
        person_user -> component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components_Pages "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Controllers_CSharp_Controllers_Areas_Admin_Pages "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Admin "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Pages_Shop "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Conventional_CSharp_Conventional_CSharp_Conventional_Controllers "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Dapr_CSharp_Dapr_ "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Delegated_CSharp_Delegated_CSharp_Delegated "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_ "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Chat "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Configured "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Orders "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Orders "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Grpc_CSharp_Grpc_ "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Middleware_CSharp_Middleware_CSharp_Middleware "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Orleans_CSharp_Orleans_ "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Switch_CSharp_Switch_CSharp_Switch "sends HTTP requests to" "HTTP"
        person_user -> component_CSharp_Table_CSharp_Table_CSharp_Table "sends HTTP requests to" "HTTP"
    }

    views {
        // Each view is laid out as on Floorplan's website: the positions are in workspace.json beside this file,
        // which Structurizr Lite merges in. Without it, add `autoLayout` to a view.
        systemContext system "context" {
            include system external_database external_filesystem external_http external_http_api_example_org external_http_api_github_com external_http_billing external_http_inventory external_http_payments_example_org external_http_pricing external_http_shipping external_http_status_example_org external_library_Amazon_DynamoDBv2 external_library_Amazon_S3 external_library_Amazon_SQS external_library_Azure_Messaging_ServiceBus external_library_Azure_Storage_Blobs external_library_Confluent_Kafka external_library_Dapr external_library_Elastic_Clients_Elasticsearch external_library_Grpc_Core external_library_MailKit external_library_MassTransit external_library_Microsoft_AspNetCore_SignalR_Client external_library_Microsoft_Azure_Cosmos external_library_Microsoft_Azure_Functions_Worker external_library_Microsoft_Azure_WebJobs external_library_Microsoft_EntityFrameworkCore external_library_MongoDB_Driver external_library_NServiceBus external_library_Orleans external_library_RabbitMQ_Client external_library_SendGrid external_library_StackExchange_Redis external_native external_network external_processes person_user
        }
        container system "containers" {
            include container_CSharp_Blazor container_CSharp_Clients container_CSharp_Cloud container_CSharp_Controllers container_CSharp_Conventional container_CSharp_Dapr container_CSharp_Delegated container_CSharp_Effects container_CSharp_Endpoints container_CSharp_Functions container_CSharp_FunctionsInProcess container_CSharp_Grpc container_CSharp_Messaging container_CSharp_Middleware container_CSharp_Orleans container_CSharp_Switch container_CSharp_Table container_CSharp_Worker external_database external_filesystem external_http external_http_api_example_org external_http_api_github_com external_http_billing external_http_inventory external_http_payments_example_org external_http_pricing external_http_shipping external_http_status_example_org external_library_Amazon_DynamoDBv2 external_library_Amazon_S3 external_library_Amazon_SQS external_library_Azure_Messaging_ServiceBus external_library_Azure_Storage_Blobs external_library_Confluent_Kafka external_library_Dapr external_library_Elastic_Clients_Elasticsearch external_library_Grpc_Core external_library_MailKit external_library_MassTransit external_library_Microsoft_AspNetCore_SignalR_Client external_library_Microsoft_Azure_Cosmos external_library_Microsoft_Azure_Functions_Worker external_library_Microsoft_Azure_WebJobs external_library_Microsoft_EntityFrameworkCore external_library_MongoDB_Driver external_library_NServiceBus external_library_Orleans external_library_RabbitMQ_Client external_library_SendGrid external_library_StackExchange_Redis external_native external_network external_processes person_user
        }
        component container_CSharp_Blazor "components_CSharp_Blazor" {
            include component_CSharp_Blazor_CSharp_Blazor_ component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components component_CSharp_Blazor_CSharp_Blazor_CSharp_Blazor_Components_Pages external_filesystem person_user
        }
        component container_CSharp_Clients "components_CSharp_Clients" {
            include component_CSharp_Clients_CSharp_Clients_ component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Caching component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Catalog component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Chat component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Documents component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Files component_CSharp_Clients_CSharp_Clients_CSharp_Clients_GitHub component_CSharp_Clients_CSharp_Clients_CSharp_Clients_Notifications external_filesystem external_http external_http_api_github_com external_http_status_example_org external_library_Azure_Storage_Blobs external_library_MailKit external_library_Microsoft_AspNetCore_SignalR_Client external_library_MongoDB_Driver external_library_SendGrid external_library_StackExchange_Redis external_network
        }
        component container_CSharp_Cloud "components_CSharp_Cloud" {
            include component_CSharp_Cloud_CSharp_Cloud_ component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Catalog component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Jobs component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Orders component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Reports component_CSharp_Cloud_CSharp_Cloud_CSharp_Cloud_Search external_filesystem external_http external_library_Amazon_DynamoDBv2 external_library_Amazon_S3 external_library_Amazon_SQS external_library_Elastic_Clients_Elasticsearch external_library_Microsoft_Azure_Cosmos external_network external_processes
        }
        component container_CSharp_Controllers "components_CSharp_Controllers" {
            include component_CSharp_Controllers_CSharp_Controllers_ component_CSharp_Controllers_CSharp_Controllers_Areas_Admin_Pages component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Admin component_CSharp_Controllers_CSharp_Controllers_CSharp_Controllers_Pages_Shop component_CSharp_Controllers_CSharp_Controllers_Pages_Shop component_CSharp_Controllers_CSharp_Controllers_Views_Pages external_filesystem person_user
        }
        component container_CSharp_Conventional "components_CSharp_Conventional" {
            include component_CSharp_Conventional_CSharp_Conventional_ component_CSharp_Conventional_CSharp_Conventional_CSharp_Conventional_Controllers person_user
        }
        component container_CSharp_Dapr "components_CSharp_Dapr" {
            include component_CSharp_Dapr_CSharp_Dapr_ component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Checkout component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Payouts component_CSharp_Dapr_CSharp_Dapr_CSharp_Dapr_Receipts external_filesystem external_http_billing external_http_inventory external_http_pricing external_http_shipping external_library_Dapr person_user
        }
        component container_CSharp_Delegated "components_CSharp_Delegated" {
            include component_CSharp_Delegated_CSharp_Delegated_ component_CSharp_Delegated_CSharp_Delegated_CSharp_Delegated person_user
        }
        component container_CSharp_Effects "components_CSharp_Effects" {
            include component_CSharp_Effects_CSharp_Effects_ component_CSharp_Effects_CSharp_Effects_CSharp_Effects component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Billing component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Labels component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Loading component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Logging component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Ordering component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Packing component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Printing component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Reporting component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Shipping component_CSharp_Effects_CSharp_Effects_CSharp_Effects_Storage external_database external_filesystem external_http external_http_api_example_org external_library_Microsoft_EntityFrameworkCore external_native external_network external_processes
        }
        component container_CSharp_Endpoints "components_CSharp_Endpoints" {
            include component_CSharp_Endpoints_CSharp_Endpoints_ component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Auditing component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Chat component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Configured component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Filtered component_CSharp_Endpoints_CSharp_Endpoints_CSharp_Endpoints_Shipping external_filesystem person_user
        }
        component container_CSharp_Functions "components_CSharp_Functions" {
            include component_CSharp_Functions_CSharp_Functions_ component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Maintenance component_CSharp_Functions_CSharp_Functions_CSharp_Functions_Orders external_filesystem external_library_Microsoft_Azure_Functions_Worker person_user
        }
        component container_CSharp_FunctionsInProcess "components_CSharp_FunctionsInProcess" {
            include component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Maintenance component_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_CSharp_FunctionsInProcess_Orders external_filesystem external_library_Microsoft_Azure_WebJobs person_user
        }
        component container_CSharp_Grpc "components_CSharp_Grpc" {
            include component_CSharp_Grpc_CSharp_Grpc_ component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Inventory component_CSharp_Grpc_CSharp_Grpc_CSharp_Grpc_Restocking external_filesystem external_library_Grpc_Core person_user
        }
        component container_CSharp_Messaging "components_CSharp_Messaging" {
            include component_CSharp_Messaging_CSharp_Messaging_ component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Configured component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Orders component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Payments component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Publishing component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Refunds component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Sales component_CSharp_Messaging_CSharp_Messaging_CSharp_Messaging_Shipping external_filesystem external_library_Azure_Messaging_ServiceBus external_library_Confluent_Kafka external_library_MassTransit external_library_NServiceBus external_library_RabbitMQ_Client external_native external_network external_processes
        }
        component container_CSharp_Middleware "components_CSharp_Middleware" {
            include component_CSharp_Middleware_CSharp_Middleware_ component_CSharp_Middleware_CSharp_Middleware_CSharp_Middleware person_user
        }
        component container_CSharp_Orleans "components_CSharp_Orleans" {
            include component_CSharp_Orleans_CSharp_Orleans_ component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Orders component_CSharp_Orleans_CSharp_Orleans_CSharp_Silo_Stock external_filesystem external_http_payments_example_org external_library_Orleans person_user
        }
        component container_CSharp_Switch "components_CSharp_Switch" {
            include component_CSharp_Switch_CSharp_Switch_ component_CSharp_Switch_CSharp_Switch_CSharp_Switch person_user
        }
        component container_CSharp_Table "components_CSharp_Table" {
            include component_CSharp_Table_CSharp_Table_ component_CSharp_Table_CSharp_Table_CSharp_Table person_user
        }
        component container_CSharp_Worker "components_CSharp_Worker" {
            include component_CSharp_Worker_CSharp_Worker_ component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Cleanup component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Imports component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Monitoring component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Queues component_CSharp_Worker_CSharp_Worker_CSharp_Worker_Reporting external_filesystem external_http
        }

        styles {
            element "Person" {
                shape Person
                background #08427b
                color #ffffff
            }
            element "Software System" {
                background #1168bd
                color #ffffff
            }
            element "Container" {
                background #438dd5
                color #ffffff
            }
            element "Component" {
                background #85bbf0
                color #000000
            }
            element "External" {
                background #999999
                color #ffffff
            }
        }
    }
}
