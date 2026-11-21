module Shop.Core.Orders

open Shop.Core.Domain

module Validation =
    let isValid (order: Order) = order.Quantity > 0

let summarize (orders: Order list) =
    orders |> List.filter Validation.isValid |> List.map Util.describe

/// Each application implements it in its own way.
type INotifier =
    abstract Notify: string -> unit

let announce (notifier: INotifier) (order: Order) = notifier.Notify(Util.describe order)
