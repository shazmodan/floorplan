// Azure Functions in the in-process model: the host loads this class library and runs each method marked
// [FunctionName] when the trigger on one of its parameters fires. HTTP routes are under host.json's routePrefix
// ("api", the default, here).
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace CSharp.FunctionsInProcess.Orders
{
    public static class OrderFunctions
    {
        [FunctionName("GetOrder")]
        public static IActionResult Get([HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/{id}")] HttpRequest req, string id)
        {
            File.AppendAllText("reads.log", id);
            return new OkResult();
        }

        // No route: the function's name is the route. Two methods.
        [FunctionName("PlaceOrder")]
        public static IActionResult Place([HttpTrigger(AuthorizationLevel.Anonymous, "post", "put")] HttpRequest req) =>
            new AcceptedResult();

        [FunctionName("OnOrder")]
        public static void OnOrder([QueueTrigger("orders")] string message) => File.AppendAllText("orders.log", message);

        // Not a function: no [FunctionName] attribute.
        public static void Helper() => File.Delete("helper.txt");
    }
}

namespace CSharp.FunctionsInProcess.Maintenance
{
    public class Cleanup
    {
        [FunctionName("Cleanup")]
        public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer) => File.Delete("tmp.txt");
    }
}
