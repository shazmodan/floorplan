/// Shares its name with Shop.Data's Util module on purpose.
module Util

let describe (order: Shop.Core.Domain.Order) = $"{order.Item} x{order.Quantity}"
