// The orders Orders keeps, and its stock, through Entity Framework Core: two contexts on two databases, each
// named where Orders.Api registers it. Each call goes to its own context's database.
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

    public class Item
    {
        public int Id { get; set; }
        public int Count { get; set; }
    }

    public class StockContext(DbContextOptions<StockContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    // A repository given its context: its calls are the stock database's.
    public class StockLog(StockContext db)
    {
        public int Take(int id)
        {
            var item = db.Items.First(i => i.Id == id);
            item.Count--;
            return db.SaveChanges();
        }
    }
}
