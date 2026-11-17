namespace CSharp.Wrappers;

/// A wrapper over HttpClient.
public static class Http
{
    /// The argument is the URL.
    public static Task<string> Fetch(HttpClient client, string url) => client.GetStringAsync(url);
}
