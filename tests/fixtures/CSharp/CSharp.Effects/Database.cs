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

    /// SQL built from a value: a template.
    public static int Purge(string table)
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table}";
        return command.ExecuteNonQuery();
    }

    /// A command made elsewhere: its SQL is not known here, so it stays dynamic.
    public static int Run(System.Data.Common.DbCommand command) => command.ExecuteNonQuery();
}
