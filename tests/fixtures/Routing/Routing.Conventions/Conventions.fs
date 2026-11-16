/// Application model conventions: one that changes no routes, and attributes that do.
namespace Routing.Conventions

open System
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.ApplicationModels

/// Registered for the whole application, but only marks controllers: routes stay as declared.
type AuditConvention() =
    interface IApplicationModelConvention with
        member _.Apply(application: ApplicationModel) =
            for controller in application.Controllers do
                controller.Properties["audited"] <- box true

/// On a controller: replaces its routes with a versioned one.
[<AttributeUsage(AttributeTargets.Class)>]
type VersionedAttribute(version: string) =
    inherit Attribute()

    interface IControllerModelConvention with
        member _.Apply(controller: ControllerModel) =
            for selector in controller.Selectors do
                selector.AttributeRouteModel <- AttributeRouteModel(RouteAttribute(version + "/[controller]"))

/// On an action: renames it, which changes the [action] token.
[<AttributeUsage(AttributeTargets.Method)>]
type LegacyNameAttribute() =
    inherit Attribute()

    interface IActionModelConvention with
        member _.Apply(action: ActionModel) = action.ActionName <- "legacy-" + action.ActionName
