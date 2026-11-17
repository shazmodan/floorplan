using Dapper;
using Flurl.Http;
using Microsoft.Data.Sqlite;

namespace CSharp.Effects;

/// Effects through wrapper libraries: the call site is in user code, the primitive inside a dependency.
public static class Wrapped
{
    public static List<string> QueryNames()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        return connection.Query<string>("SELECT name FROM users").ToList();
    }

    public static string FetchUsers() => "https://api.example.org/users".GetStringAsync().Result;
}
