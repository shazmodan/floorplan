/// A convention registered for the whole application that puts every controller route under a prefix.
namespace Routing.Prefixed

open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.ApplicationModels

type RoutePrefixConvention(prefix: string) =
    let model = AttributeRouteModel(RouteAttribute(prefix))

    interface IApplicationModelConvention with
        member _.Apply(application: ApplicationModel) =
            for controller in application.Controllers do
                for selector in controller.Selectors do
                    selector.AttributeRouteModel <-
                        if isNull selector.AttributeRouteModel then model
                        else AttributeRouteModel.CombineAttributeRouteModel(model, selector.AttributeRouteModel)

/// Declared at /items, served at /api/items: the analysis does not follow the prefix, so the route is partial.
[<Route("items")>]
type ItemsController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.List() = "items"
