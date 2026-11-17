// A switch expression on a tuple of method and path (plan section 6.5), behind HttpListener.
using System.Net;
using CSharp.Switch;

var listener = new HttpListener();
listener.Prefixes.Add("http://localhost:8083/");
listener.Start();

while (true)
{
    var context = listener.GetContext();
    var request = context.Request;
    var body = Router.Route(request.HttpMethod, request.Url!.AbsolutePath);
    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    context.Response.Close();
}
