namespace CSharp.Wrappers;

/// A wrapper over HttpClient.
public static class Http
{
    /// The argument is the URL.
    public static Task<string> Fetch(HttpClient client, string url) => client.GetStringAsync(url);

    /// The URL given to a request's constructor.
    public static Task<HttpResponseMessage> Get(HttpClient client, string url) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

    /// The URL set on a request.
    public static Task<HttpResponseMessage> Delete(HttpClient client, string url)
    {
        var request = new HttpRequestMessage { Method = HttpMethod.Delete, RequestUri = new Uri(url) };
        return client.SendAsync(request);
    }
}
