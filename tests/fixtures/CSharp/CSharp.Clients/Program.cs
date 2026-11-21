// Clients of databases and storage: each call that runs a command is an exit point.
using Azure.Storage.Blobs;
using MongoDB.Driver;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost");
await new CSharp.Clients.Caching.Cache(redis).Remember("greeting", "hello");
CSharp.Clients.Caching.Cache.Listen(redis);
await new CSharp.Clients.Documents.Store(new MongoClient("mongodb://localhost")).Save("doc");
await new CSharp.Clients.Files.Archive(new BlobContainerClient("UseDevelopmentStorage=true", "files")).Keep("a.txt");

namespace CSharp.Clients.Caching
{
    public class Cache(IConnectionMultiplexer redis)
    {
        public async Task Remember(string key, string value)
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync(key, value);
            await db.PublishAsync(RedisChannel.Literal("cache"), key);
        }

        public string? Recall(string key) => redis.GetDatabase().StringGet(key);

        // A constant key: the string converted to a RedisKey.
        public long Visit() => redis.GetDatabase().StringIncrement("visits");

        // Redis pub/sub: the handler runs for each message on the channel.
        public static void Listen(IConnectionMultiplexer redis) =>
            redis.GetSubscriber().Subscribe(RedisChannel.Literal("cache"), (_, message) => File.AppendAllText("cache.log", message.ToString()));
    }
}

namespace CSharp.Clients.Documents
{
    public class Doc
    {
        public string Name { get; set; } = "";
    }

    public class Store(IMongoClient client)
    {
        private IMongoCollection<Doc> Docs => client.GetDatabase("shop").GetCollection<Doc>("docs");

        public Task Save(string name) => Docs.InsertOneAsync(new Doc { Name = name });

        public Task<List<Doc>> Named(string name) => Docs.Find(d => d.Name == name).ToListAsync();

        public Task<DeleteResult> Forget(string name) => Docs.DeleteOneAsync(d => d.Name == name);
    }
}

namespace CSharp.Clients.Catalog
{
    using Refit;

    public record Product(string Id, string Name);

    // Refit: each method the interface declares with a verb attribute is an HTTP call to its path.
    public interface ICatalogApi
    {
        [Get("/products/{id}")]
        Task<Product> GetProduct(string id);

        [Post("/products")]
        Task AddProduct([Body] Product product);
    }

    public class Products(ICatalogApi api)
    {
        public Task<Product> Find(string id) => api.GetProduct(id);

        public Task Add(string name) => api.AddProduct(new Product(Guid.NewGuid().ToString(), name));

        public static ICatalogApi Connect() => RestService.For<ICatalogApi>("https://catalog.example.org");
    }
}

namespace CSharp.Clients.Files
{
    public class Archive(BlobContainerClient container)
    {
        public async Task Keep(string path)
        {
            await using var file = File.OpenRead(path);
            await container.UploadBlobAsync("archive/" + Path.GetFileName(path), file);
        }

        public Task<Azure.Response<Azure.Storage.Blobs.Models.BlobDownloadResult>> Fetch(string name) =>
            container.GetBlobClient(name).DownloadContentAsync();
    }
}
