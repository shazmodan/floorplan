// AWS clients: each operation is an exit point, with the bucket, table or queue its request names. A loop that
// receives from a queue is an entry point at the queue. Azure Cosmos DB and Elasticsearch: each request the client
// sends is a database exit point.
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

await new CSharp.Cloud.Reports.Archive(new AmazonS3Client()).Store("daily.csv", "a,b");
await new CSharp.Cloud.Orders.Table(new AmazonDynamoDBClient()).Save("1");
await CSharp.Cloud.Jobs.Queue.Run(new AmazonSQSClient(), CancellationToken.None);

var cosmos = new Microsoft.Azure.Cosmos.CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=a2V5");
var products = new CSharp.Cloud.Catalog.Products(cosmos.GetContainer("shop", "products"));
await products.Save(new CSharp.Cloud.Catalog.Product("1", "Chair"));
await products.All();
await products.Restock("1");

var search = new CSharp.Cloud.Search.ProductIndex(new Elastic.Clients.Elasticsearch.ElasticsearchClient(new Uri("http://localhost:9200")));
await search.Add(new CSharp.Cloud.Catalog.Product("1", "Chair"));
await search.Find("chair");

namespace CSharp.Cloud.Reports
{
    public class Archive(IAmazonS3 s3)
    {
        public Task<PutObjectResponse> Store(string key, string body) =>
            s3.PutObjectAsync(new PutObjectRequest { BucketName = "reports", Key = key, ContentBody = body });

        public Task<GetObjectResponse> Load(string key) => s3.GetObjectAsync(new GetObjectRequest { BucketName = "reports", Key = key });
    }
}

namespace CSharp.Cloud.Orders
{
    public class Table(IAmazonDynamoDB dynamo)
    {
        public Task<PutItemResponse> Save(string id) =>
            dynamo.PutItemAsync(new PutItemRequest { TableName = "orders", Item = new() { ["id"] = new AttributeValue(id) } });
    }
}

namespace CSharp.Cloud.Catalog
{
    using Microsoft.Azure.Cosmos;

    public record Product(string id, string Name);

    public class Products(Container container)
    {
        public Task<ItemResponse<Product>> Save(Product product) => container.UpsertItemAsync(product, new PartitionKey(product.id));

        // A query runs a page at a time, each page a request.
        public async Task<List<Product>> All()
        {
            var all = new List<Product>();
            using var pages = container.GetItemQueryIterator<Product>("SELECT * FROM c");

            while (pages.HasMoreResults)
                all.AddRange(await pages.ReadNextAsync());

            return all;
        }

        // A transactional batch sends its operations when executed.
        public Task<TransactionalBatchResponse> Restock(string id) =>
            container.CreateTransactionalBatch(new PartitionKey(id)).DeleteItem(id).ExecuteAsync();
    }
}

namespace CSharp.Cloud.Search
{
    using CSharp.Cloud.Catalog;
    using Elastic.Clients.Elasticsearch;

    public class ProductIndex(ElasticsearchClient client)
    {
        public Task<IndexResponse> Add(Product product) => client.IndexAsync(product, (IndexName)"products");

        public Task<SearchResponse<Product>> Find(string text) =>
            client.SearchAsync<Product>(new SearchRequest("products") { Query = new Elastic.Clients.Elasticsearch.QueryDsl.MatchQuery("name") { Query = text } });
    }
}

namespace CSharp.Cloud.Jobs
{
    public static class Queue
    {
        private const string Url = "https://sqs.eu-north-1.amazonaws.com/123456789012/jobs";

        public static Task Enqueue(IAmazonSQS sqs, string job) => sqs.SendMessageAsync(Url, job);

        public static async Task Run(IAmazonSQS sqs, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = Url }, ct);

                foreach (var message in received.Messages)
                    await File.AppendAllTextAsync("jobs.log", message.Body, ct);
            }
        }
    }
}
