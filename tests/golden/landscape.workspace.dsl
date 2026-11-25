workspace "Landscape" "Landscape, derived by Floorplan from its code" {
    model {
        !impliedRelationships "com.structurizr.model.CreateImpliedRelationshipsUnlessSameRelationshipExistsStrategy"
        system_billing = softwareSystem "Billing" "The Billing solution: 1 applications in 1 projects" {
            container_billing_Billing_Api = container "Billing.Api" "A web application" "C# · net8.0" "web application"
        }
        system_orders = softwareSystem "Orders" "The Orders solution: 1 applications in 1 projects" {
            container_orders_Orders_Api = container "Orders.Api" "A web application" "C# · net8.0" "web application"
        }
        system_shipping = softwareSystem "Shipping" "The Shipping solution: 1 applications in 1 projects" {
            container_shipping_Shipping_Worker = container "Shipping.Worker" "A message consumer" "C# · net8.0" "message consumer"
        }
        external_database_db_internal_5432_ledger = softwareSystem "ledger on db.internal:5432" "A database more than one system uses" "External,shared database"
        external_folder__srv_exchange = softwareSystem "/srv/exchange" "A folder more than one system uses" "External,shared folder"
        external_http_inventory_example_net = softwareSystem "inventory.example.net" "An HTTP service" "External"
        external_library_RabbitMQ_Client = softwareSystem "RabbitMQ.Client" "A service reached through its client library" "External"
        external_network_orders = softwareSystem "Network (Orders)" "Hosts reached over the network" "External"
        external_network_shipping = softwareSystem "Network (Shipping)" "Hosts reached over the network" "External"
        person_user = person "User" "A person or client calling the HTTP endpoints"

        container_billing_Billing_Api -> container_orders_Orders_Api "sends HTTP requests to /orders/{id}" "HTTP" "Probable"
        container_billing_Billing_Api -> external_database_db_internal_5432_ledger "reads and writes data"
        container_billing_Billing_Api -> external_folder__srv_exchange "writes files"
        container_orders_Orders_Api -> container_billing_Billing_Api "sends HTTP requests to /invoices/{id}" "HTTP"
        container_orders_Orders_Api -> container_shipping_Shipping_Worker "sends messages to shipments" "RabbitMQ.Client"
        container_orders_Orders_Api -> external_http_inventory_example_net "sends HTTP requests" "HTTP"
        container_orders_Orders_Api -> external_network_orders "sends and receives over the network" "RabbitMQ.Client"
        container_shipping_Shipping_Worker -> container_orders_Orders_Api "sends HTTP requests to /orders/{id}/delivered" "HTTP"
        container_shipping_Shipping_Worker -> external_database_db_internal_5432_ledger "reads and writes data"
        container_shipping_Shipping_Worker -> external_folder__srv_exchange "writes files"
        container_shipping_Shipping_Worker -> external_network_shipping "sends and receives over the network" "RabbitMQ.Client"
        external_library_RabbitMQ_Client -> container_shipping_Shipping_Worker "delivers messages to"
        person_user -> container_billing_Billing_Api "sends HTTP requests to" "HTTP"
        person_user -> container_orders_Orders_Api "sends HTTP requests to" "HTTP"
    }

    views {
        // Each view is laid out as on Floorplan's website: the positions are in workspace.json beside this file,
        // which Structurizr Lite merges in. Without it, add `autoLayout` to a view.
        systemLandscape "context" {
            include system_billing system_orders system_shipping external_database_db_internal_5432_ledger external_folder__srv_exchange person_user
        }
        systemLandscape "context_all" {
            include system_billing system_orders system_shipping external_database_db_internal_5432_ledger external_folder__srv_exchange external_http_inventory_example_net external_library_RabbitMQ_Client external_network_orders external_network_shipping person_user
        }
        container system_billing "containers_billing" {
            include container_billing_Billing_Api container_orders_Orders_Api external_database_db_internal_5432_ledger external_folder__srv_exchange person_user
        }
        container system_orders "containers_orders" {
            include container_orders_Orders_Api container_billing_Billing_Api container_shipping_Shipping_Worker external_http_inventory_example_net external_network_orders person_user
        }
        container system_shipping "containers_shipping" {
            include container_shipping_Shipping_Worker container_orders_Orders_Api external_database_db_internal_5432_ledger external_folder__srv_exchange external_library_RabbitMQ_Client external_network_shipping
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
            relationship "Probable" {
                dashed true
            }
        }
    }
}
