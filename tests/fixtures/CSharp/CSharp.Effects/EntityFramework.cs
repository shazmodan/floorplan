using Microsoft.EntityFrameworkCore;

// Entity Framework Core runs its commands through its own service container, so its code is not followed to the
// database: saving changes, raw SQL, bulk updates and deletes, and the queries that run, are calls to the database.
namespace CSharp.Effects.Ordering
{
    public class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    public class OrderStore(ShopContext db)
    {
        public void Add(string name)
        {
            db.Orders.Add(new Order { Name = name });
            db.SaveChanges();
        }

        public Task<int> AddAsync(string name)
        {
            db.Orders.Add(new Order { Name = name });
            return db.SaveChangesAsync();
        }

        public int Purge() => db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE Id < 0");

        public int Rename(int id, string name) => db.Database.ExecuteSqlInterpolated($"UPDATE Orders SET Name = {name} WHERE Id = {id}");

        public int Archive() => db.Orders.Where(o => o.Id < 10).ExecuteDelete();

        public Task<List<Order>> ListAsync() => db.Orders.ToListAsync();

        public ValueTask<Order?> FindAsync(int id) => db.Orders.FindAsync(id);

        // Queries that run when they are enumerated.
        public List<Order> All() => db.Orders.ToList();

        public int Count() => db.Orders.Count(o => o.Id > 0);

        public Order? Named(string name) => db.Orders.Where(o => o.Name == name).AsNoTracking().FirstOrDefault();

        public List<string> Names()
        {
            var names = new List<string>();

            foreach (var order in db.Orders.OrderBy(o => o.Name))
                names.Add(order.Name);

            return names;
        }

        public async Task<int> Stream()
        {
            var n = 0;

            await foreach (var _ in db.Orders.AsAsyncEnumerable())
                n++;

            return n;
        }

        public List<Order> Raw() => db.Orders.FromSqlRaw("SELECT * FROM Orders").ToList();

        public Task<List<Order>> RawAsync(int id) => db.Orders.FromSql($"SELECT * FROM Orders WHERE Id = {id}").ToListAsync();

        public List<int> Ids() => db.Database.SqlQueryRaw<int>("SELECT Id FROM Orders").ToList();

        // LINQ over a list is not a query.
        public List<Order> Local(List<Order> orders) => orders.Where(o => o.Id > 0).ToList();
    }
}

// Queries made in one method and run in another: a repository returns them, an extension method builds on them.
namespace CSharp.Effects.Reporting
{
    using CSharp.Effects.Ordering;

    public interface IOrders
    {
        IQueryable<Order> All();
    }

    public class OrderRepository(ShopContext db) : IOrders
    {
        public IQueryable<Order> All() => db.Orders;

        public IQueryable<Order> Matching(string sql) => db.Orders.FromSqlRaw(sql);

        public static IOrders Create(ShopContext db) => new OrderRepository(db);
    }

    public static class OrderQueries
    {
        public static IQueryable<Order> Named(this IQueryable<Order> orders, string name) => orders.Where(o => o.Name == name);
    }

    public class OrderReports(OrderRepository repository, IOrders orders)
    {
        public List<Order> All() => repository.All().ToList();

        public int Count() => orders.All().Count();

        public Order? Named(string name) => repository.All().Named(name).FirstOrDefault();

        public List<Order> Cancelled() => repository.Matching("SELECT * FROM Orders WHERE Cancelled = 1").ToList();

        // A list made into a queryable is not a query.
        public List<Order> Local(List<Order> list) => list.AsQueryable().Named("x").ToList();
    }
}

// Queries passed to a method that enumerates them: they run at the call that passes them.
namespace CSharp.Effects.Printing
{
    using CSharp.Effects.Ordering;
    using CSharp.Wrappers;

    public static class Printer
    {
        public static int Print(IEnumerable<Order> orders)
        {
            var n = 0;

            foreach (var _ in orders)
                n++;

            return n;
        }

        public static List<string> Names(IQueryable<Order> orders) => orders.Where(o => o.Id > 0).Select(o => o.Name).ToList();

        public static int Again(IEnumerable<Order> orders) => Print(orders);

        // Kept in the async method's state machine, and in a lambda's closure.
        public static async Task<int> PrintAsync(IEnumerable<Order> orders)
        {
            await Task.Yield();
            var n = 0;

            foreach (var _ in orders)
                n++;

            return n;
        }

        public static Task<List<Order>> Later(IEnumerable<Order> orders) => Task.Run(() => orders.ToList());

        // Framework methods that enumerate an argument other than the first.
        public static string Joined(IEnumerable<Order> orders) => string.Join(", ", orders.Select(o => o.Name));
    }

    public class Summary(IEnumerable<Order> orders)
    {
        public List<Order> Items { get; } = orders.ToList();
    }

    public class OrderPrinting(ShopContext db)
    {
        public int Print() => Printer.Print(db.Orders);

        public List<string> Shipped() => Printer.Names(db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE Shipped = 1"));

        public int Again() => Printer.Again(db.Orders.Where(o => o.Id > 3));

        public Summary Summarize() => new Summary(db.Orders);

        public int Counted() => Sequences.CountAll(db.Orders);

        public Task<int> PrintAsync() => Printer.PrintAsync(db.Orders);

        public Task<List<Order>> Later() => Printer.Later(db.Orders.Where(o => o.Id > 5));

        public string Listed() => Printer.Joined(db.Orders);

        public string Names() => string.Concat(db.Orders.Select(o => o.Name));

        public List<Order> Added()
        {
            var all = new List<Order>();
            all.AddRange(db.Orders);
            return all;
        }

        // A list is not a query.
        public int Local(List<Order> list) => Printer.Print(list);
    }
}
