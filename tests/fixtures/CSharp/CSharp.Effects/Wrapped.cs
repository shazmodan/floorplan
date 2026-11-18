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

    public static int DeleteSessions()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        return connection.Execute("DELETE FROM sessions");
    }

    public static async Task<string> QueryNameAsync()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        return await connection.QuerySingleAsync<string>("SELECT name FROM users WHERE id = 1");
    }

    public static async Task<List<string>> QueryNamesAsync()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        return (await connection.QueryAsync<string>("SELECT name FROM users")).ToList();
    }

    public static async Task<int> CountSessionsAsync()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sessions");
    }

    public static Task<int> DeleteSessionsAsync()
    {
        var connection = new SqliteConnection("Data Source=app.db");
        return connection.ExecuteAsync("DELETE FROM sessions");
    }

    public static string FetchUsers() => "https://api.example.org/users".GetStringAsync().Result;
}
