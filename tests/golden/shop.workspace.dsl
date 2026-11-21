workspace "Shop" "Shop, derived by Floorplan from its code" {
    model {
        !impliedRelationships "com.structurizr.model.CreateImpliedRelationshipsUnlessSameRelationshipExistsStrategy"
        system = softwareSystem "Shop" "The Shop solution: 2 applications in 5 projects" {
            container_Shop_Web = container "Shop.Web" "A web application" "F# · net10.0" "web application" {
                component_Shop_Web_Shop_Core_Shop_Core_Domain = component "Shop.Core.Domain" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Web_Shop_Core_Shop_Core_Orders = component "Shop.Core.Orders" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Web_Shop_Core_Shop_Core_Orders_Validation = component "Shop.Core.Orders.Validation" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Web_Shop_Core_Util = component "Util (Shop.Core)" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Web_Shop_Data_Shop_Data_Repository = component "Shop.Data.Repository" "An F# module of Shop.Data" "F# module" "library Shop.Data,reaches out"
                component_Shop_Web_Shop_Data_Util = component "Util (Shop.Data)" "An F# module of Shop.Data" "F# module" "library Shop.Data"
                component_Shop_Web_Shop_Storage_Shop_Storage = component "Shop.Storage" "A C# namespace of Shop.Storage" "C# namespace" "library Shop.Storage,reaches out"
                component_Shop_Web_Shop_Web_Shop_Web_Handlers = component "Shop.Web.Handlers" "An F# module of Shop.Web" "F# module" "handles requests,reaches out"
                component_Shop_Web_Shop_Web_Shop_Web_Program = component "Shop.Web.Program" "An F# module of Shop.Web" "F# module"
            }
            container_Shop_Worker = container "Shop.Worker" "A background worker" "F# · net8.0" "background worker" {
                component_Shop_Worker_Shop_Core_Shop_Core_Domain = component "Shop.Core.Domain" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Worker_Shop_Core_Shop_Core_Orders = component "Shop.Core.Orders" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Worker_Shop_Core_Shop_Core_Orders_Validation = component "Shop.Core.Orders.Validation" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Worker_Shop_Core_Util = component "Util" "An F# module of Shop.Core" "F# module" "library Shop.Core"
                component_Shop_Worker_Shop_Worker_Shop_Worker_Program = component "Shop.Worker.Program" "An F# module of Shop.Worker" "F# module" "reaches out"
            }
        }
        external_filesystem = softwareSystem "File system" "Files and directories" "External"
        external_http_prices_example_org = softwareSystem "prices.example.org" "An HTTP service" "External"
        external_processes = softwareSystem "Operating system" "Processes the application starts or inspects" "External"
        person_user = person "User" "A person or client calling the HTTP endpoints"

        component_Shop_Web_Shop_Core_Shop_Core_Orders -> component_Shop_Web_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Web_Shop_Core_Shop_Core_Orders -> component_Shop_Web_Shop_Core_Shop_Core_Orders_Validation "uses"
        component_Shop_Web_Shop_Core_Shop_Core_Orders -> component_Shop_Web_Shop_Core_Util "uses"
        component_Shop_Web_Shop_Core_Shop_Core_Orders_Validation -> component_Shop_Web_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Web_Shop_Core_Util -> component_Shop_Web_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Web_Shop_Data_Shop_Data_Repository -> component_Shop_Web_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Web_Shop_Data_Shop_Data_Repository -> component_Shop_Web_Shop_Data_Util "uses"
        component_Shop_Web_Shop_Data_Shop_Data_Repository -> component_Shop_Web_Shop_Storage_Shop_Storage "uses"
        component_Shop_Web_Shop_Data_Shop_Data_Repository -> external_filesystem "reads files"
        component_Shop_Web_Shop_Storage_Shop_Storage -> external_filesystem "writes files"
        component_Shop_Web_Shop_Web_Shop_Web_Handlers -> component_Shop_Web_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Web_Shop_Web_Shop_Web_Handlers -> component_Shop_Web_Shop_Core_Shop_Core_Orders "uses"
        component_Shop_Web_Shop_Web_Shop_Web_Handlers -> component_Shop_Web_Shop_Data_Shop_Data_Repository "uses"
        component_Shop_Web_Shop_Web_Shop_Web_Handlers -> external_filesystem "writes files"
        component_Shop_Web_Shop_Web_Shop_Web_Handlers -> external_http_prices_example_org "sends HTTP requests" "HTTP"
        component_Shop_Web_Shop_Web_Shop_Web_Program -> component_Shop_Web_Shop_Web_Shop_Web_Handlers "uses"
        component_Shop_Worker_Shop_Core_Shop_Core_Orders -> component_Shop_Worker_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Worker_Shop_Core_Shop_Core_Orders -> component_Shop_Worker_Shop_Core_Shop_Core_Orders_Validation "uses"
        component_Shop_Worker_Shop_Core_Shop_Core_Orders -> component_Shop_Worker_Shop_Core_Util "uses"
        component_Shop_Worker_Shop_Core_Shop_Core_Orders_Validation -> component_Shop_Worker_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Worker_Shop_Core_Util -> component_Shop_Worker_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> component_Shop_Web_Shop_Web_Shop_Web_Handlers "sends HTTP requests to /api/orders" "HTTP"
        component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> component_Shop_Worker_Shop_Core_Shop_Core_Domain "uses"
        component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> component_Shop_Worker_Shop_Core_Shop_Core_Orders "uses"
        component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> component_Shop_Worker_Shop_Core_Util "uses"
        component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> external_processes "starts or inspects processes"
        person_user -> component_Shop_Web_Shop_Web_Shop_Web_Handlers "sends HTTP requests to" "HTTP"
    }

    views {
        systemContext system "context" {
            include system external_filesystem external_http_prices_example_org external_processes person_user
            autoLayout tb
        }
        container system "containers" {
            include container_Shop_Web container_Shop_Worker external_filesystem external_http_prices_example_org external_processes person_user
            autoLayout tb
        }
        component container_Shop_Web "components_Shop_Web" {
            include component_Shop_Web_Shop_Core_Shop_Core_Domain component_Shop_Web_Shop_Core_Shop_Core_Orders component_Shop_Web_Shop_Core_Shop_Core_Orders_Validation component_Shop_Web_Shop_Core_Util component_Shop_Web_Shop_Data_Shop_Data_Repository component_Shop_Web_Shop_Data_Util component_Shop_Web_Shop_Storage_Shop_Storage component_Shop_Web_Shop_Web_Shop_Web_Handlers component_Shop_Web_Shop_Web_Shop_Web_Program external_filesystem external_http_prices_example_org person_user container_Shop_Worker
            autoLayout tb
        }
        component container_Shop_Worker "components_Shop_Worker" {
            include component_Shop_Worker_Shop_Core_Shop_Core_Domain component_Shop_Worker_Shop_Core_Shop_Core_Orders component_Shop_Worker_Shop_Core_Shop_Core_Orders_Validation component_Shop_Worker_Shop_Core_Util component_Shop_Worker_Shop_Worker_Shop_Worker_Program external_processes container_Shop_Web
            autoLayout tb
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
