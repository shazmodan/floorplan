// AWS clients: each operation is an exit point, with the bucket, table or queue its request names. A loop that
// receives from a queue is an entry point at the queue.
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

await new CSharp.Cloud.Reports.Archive(new AmazonS3Client()).Store("daily.csv", "a,b");
await new CSharp.Cloud.Orders.Table(new AmazonDynamoDBClient()).Save("1");
await CSharp.Cloud.Jobs.Queue.Run(new AmazonSQSClient(), CancellationToken.None);

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
