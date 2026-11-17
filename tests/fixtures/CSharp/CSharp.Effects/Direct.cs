using System.Diagnostics;

namespace CSharp.Effects;

/// Each effect category, called directly.
public static class Direct
{
    static readonly HttpClient Client = new();

    public static string Fetch() => Client.GetStringAsync("https://api.example.org/status").Result;

    public static string ReadConfig() => File.ReadAllText("config.json");

    public static void WriteReport(string text) => File.WriteAllText("report.txt", text);

    public static void RunGit() => Process.Start("git", "status");
}
