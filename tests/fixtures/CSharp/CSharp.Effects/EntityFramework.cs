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
    }
}
