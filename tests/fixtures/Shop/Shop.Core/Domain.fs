module Shop.Core.Domain

type OrderId = OrderId of int

type Order = { Id: OrderId; Item: string; Quantity: int }

let create id item quantity =
    { Id = OrderId id
      Item = item
      Quantity = quantity }
