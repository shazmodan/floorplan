// The orders Orders keeps, through Entity Framework Core. The context's database is named where Orders.Api
// registers it: these calls go to it.
using Microsoft.EntityFrameworkCore;

namespace Orders.Data
{
    public class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    public static class OrderLog
    {
        public static int Record(OrdersContext db, string name)
        {
            db.Orders.Add(new Order { Name = name });
            return db.SaveChanges();
        }
    }
}
