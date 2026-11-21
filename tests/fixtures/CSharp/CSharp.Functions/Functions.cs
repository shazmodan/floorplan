// Azure Functions: the host runs each method marked [Function] when the trigger on one of its parameters fires. HTTP
// routes are under host.json's routePrefix ("fn" here).
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;

new HostBuilder().ConfigureFunctionsWorkerDefaults().Build().Run();

namespace CSharp.Functions.Orders
{
    public class OrderFunctions
    {
        [Function("GetOrder")]
        public HttpResponseData Get([HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/{id}")] HttpRequestData req, string id)
        {
            File.AppendAllText("reads.log", id);
            return req.CreateResponse(HttpStatusCode.OK);
        }

        // No route: the function's name is the route. Two methods.
        [Function("PlaceOrder")]
        public HttpResponseData Place([HttpTrigger(AuthorizationLevel.Anonymous, "post", "put")] HttpRequestData req) =>
            req.CreateResponse(HttpStatusCode.Accepted);

        [Function("OnOrder")]
        public void OnOrder([QueueTrigger("orders")] string message) => File.AppendAllText("orders.log", message);

        // Not a function: no [Function] attribute.
        public void Helper() => File.Delete("helper.txt");
    }
}

namespace CSharp.Functions.Maintenance
{
    public class Cleanup
    {
        [Function("Cleanup")]
        public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer) => File.Delete("tmp.txt");
    }
}
