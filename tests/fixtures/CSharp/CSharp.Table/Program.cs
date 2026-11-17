// Routing by table lookup (form B) behind HttpListener: handlers stored next to constant paths.
using System.Net;
using CSharp.Table;

var listener = new HttpListener();
listener.Prefixes.Add("http://localhost:8082/");
listener.Start();

while (true)
{
    var context = listener.GetContext();
    var body = Router.Route(context.Request.Url!.AbsolutePath);
    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    context.Response.Close();
}
