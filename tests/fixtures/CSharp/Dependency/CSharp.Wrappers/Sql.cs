using System.Data.Common;

namespace CSharp.Wrappers;

/// SQL given by the caller, kept by the library in an object it is handed.
public sealed class Query(string text)
{
    public string Text { get; } = text;
}

/// SQL kept in a struct the library makes itself and passes by reference, as Dapper's CommandDefinition is.
public readonly struct Definition
{
    public Definition(string commandText) => CommandText = commandText;

    public string CommandText { get; }
}

/// A wrapper over ADO.NET: each method reaches a command whose SQL comes from the caller in a different way.
public static class Sql
{
    /// The argument is the command text.
    public static int Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    /// Passed on through another call.
    public static int Run(DbConnection connection, string sql) => Exec(connection, sql);

    /// Built inside the library from the argument.
    public static object? Count(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM " + table;
        return command.ExecuteScalar();
    }

    /// The command is made by another method, which returns it.
    public static object? Scalar(DbConnection connection, string sql)
    {
        using var command = Prepare(connection, sql);
        return command.ExecuteScalar();
    }

    private static DbCommand Prepare(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// Kept in an object the caller made.
    public static int Execute(DbConnection connection, Query query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query.Text;
        return command.ExecuteNonQuery();
    }

    /// Kept in a struct made here, passed by reference to the method that runs it.
    public static int ExecuteDefinition(DbConnection connection, string sql)
    {
        var definition = new Definition(sql);
        return ExecuteImpl(connection, in definition);
    }

    private static int ExecuteImpl(DbConnection connection, in Definition definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = definition.CommandText;
        return command.ExecuteNonQuery();
    }

    /// An async method: the argument is a field of its state machine.
    public static async Task<int> ExecAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    /// An iterator: the argument is copied into its state machine twice.
    public static IEnumerable<object> Rows(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        while (reader.Read())
            yield return reader.GetValue(0);
    }

    /// Through a dictionary: not followed, so the SQL stays dynamic.
    public static int Stored(DbConnection connection, string sql)
    {
        var statements = new Dictionary<string, string> { ["main"] = sql };
        using var command = connection.CreateCommand();
        command.CommandText = statements["main"];
        return command.ExecuteNonQuery();
    }
}
