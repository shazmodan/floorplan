using CSharp.Wrappers;
using Microsoft.Data.Sqlite;

namespace CSharp.Effects;

/// Effects behind a wrapper library (Dependency/CSharp.Wrappers), whose payloads come from these call sites.
public static class ThroughWrappers
{
    private static SqliteConnection Connect() => new("Data Source=app.db");

    public static int ExecLiteral() => Sql.Exec(Connect(), "DELETE FROM sessions");

    public static string FetchLiteral() => Http.Fetch(new HttpClient(), "https://api.example.org/status").Result;

    public static HttpResponseMessage RequestConstructed() => Http.Get(new HttpClient(), "https://api.example.org/orders").Result;

    public static HttpResponseMessage RequestProperty() => Http.Delete(new HttpClient(), "https://api.example.org/carts/1").Result;

    public static int RunChain() => Sql.Run(Connect(), "DELETE FROM carts");

    public static object? CountLiteral() => Sql.Count(Connect(), "orders");

    public static object? CountDynamic(string table) => Sql.Count(Connect(), table);

    public static object? ScalarReturned() => Sql.Scalar(Connect(), "SELECT max(id) FROM orders");

    public static int ExecuteQueryObject() => Sql.Execute(Connect(), new Query("UPDATE orders SET seen = 1"));

    public static int ExecuteDefinition() => Sql.ExecuteDefinition(Connect(), "DELETE FROM logs");

    public static int ExecAsyncLiteral() => Sql.ExecAsync(Connect(), "DELETE FROM tokens").Result;

    public static List<object> RowsLiteral() => Sql.Rows(Connect(), "SELECT name FROM users").ToList();

    public static int InLambda() => Sql.Deferred(Connect(), "DELETE FROM drafts");

    public static int InBackground() => Sql.InBackground(Connect(), "outbox").Result;

    public static int ThroughDictionary() => Sql.Stored(Connect(), "DELETE FROM sessions");

    public static int ThroughList() => Sql.Listed(Connect(), "DELETE FROM carts WHERE stale");

    public static int ThroughFilledDictionary() => Sql.Filled(Connect(), "DELETE FROM sessions WHERE expired");

    public static int ThroughFieldDictionary() => Sql.Purge(Connect());

    public static int ThroughRemembered()
    {
        Sql.Remember("DELETE FROM drafts");
        return Sql.RunRemembered(Connect());
    }

    public static void RotateLog(string[] locks) => Files.Rotate("logs/app.log", locks);

    public static int Interpolated(string id) => Sql.Exec(Connect(), $"DELETE FROM orders WHERE id = {id}");
}
