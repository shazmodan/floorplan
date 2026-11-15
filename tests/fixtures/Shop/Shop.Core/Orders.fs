module Shop.Core.Orders

open Shop.Core.Domain

module Validation =
    let isValid (order: Order) = order.Quantity > 0

let summarize (orders: Order list) =
    orders |> List.filter Validation.isValid |> List.map Util.describe
