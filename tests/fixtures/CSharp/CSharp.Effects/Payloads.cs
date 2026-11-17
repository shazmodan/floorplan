namespace CSharp.Effects;

/// Payload recovery: literals, constants, templates, and values that stay dynamic.
public static class Payloads
{
    const string DataDir = "data";

    static readonly HttpClient Client = new();

    public static string User(string id) => Client.GetStringAsync($"https://api.example.org/users/{id}").Result;

    public static string Order(int id) => Client.GetStringAsync("https://api.example.org/orders/" + id).Result;

    public static string File(string name) => System.IO.File.ReadAllText(Path.Combine(DataDir, name));

    public static string Anything(string path) => System.IO.File.ReadAllText(path);
}
