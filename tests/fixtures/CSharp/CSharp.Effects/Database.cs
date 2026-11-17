using Microsoft.Data.Sqlite;
using Npgsql;

namespace CSharp.Effects;

/// ADO.NET through two different providers, with using declarations and an object initializer.
public static class Database
{
    public static void Sqlite()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions";
        command.ExecuteNonQuery();
    }

    public static object? Postgres()
    {
        using var connection = new NpgsqlConnection("Host=localhost");
        connection.Open();
        using var command = new NpgsqlCommand { Connection = connection, CommandText = "SELECT count(*) FROM orders" };
        return command.ExecuteScalar();
    }
}
