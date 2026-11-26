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

    /// In a lambda: the argument is a field of the display class this method makes, which the delegate is bound to.
    public static int Deferred(DbConnection connection, string sql)
    {
        Func<int> run = () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteNonQuery();
        };

        return Retry(run);
    }

    private static int Retry(Func<int> run)
    {
        try
        {
            return run();
        }
        catch (DbException)
        {
            return run();
        }
    }

    /// In a lambda run on the thread pool, with text built from the argument.
    public static Task<int> InBackground(DbConnection connection, string table) =>
        Task.Run(() =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + table;
            return command.ExecuteNonQuery();
        });

    /// Through a dictionary made here, read at the key it was stored at.
    public static int Stored(DbConnection connection, string sql)
    {
        var statements = new Dictionary<string, string> { ["main"] = sql };
        using var command = connection.CreateCommand();
        command.CommandText = statements["main"];
        return command.ExecuteNonQuery();
    }

    /// Through a list made here, read at the index it was added at.
    public static int Listed(DbConnection connection, string sql)
    {
        var statements = new List<string>();
        statements.Add("SELECT 1");
        statements.Add(sql);
        using var command = connection.CreateCommand();
        command.CommandText = statements[1];
        return command.ExecuteNonQuery();
    }

    /// Through a dictionary another method fills: what is in it is not known, so the SQL stays dynamic.
    public static int Filled(DbConnection connection, string sql)
    {
        var statements = new Dictionary<string, string> { ["main"] = sql };
        Override(statements);
        using var command = connection.CreateCommand();
        command.CommandText = statements["main"];
        return command.ExecuteNonQuery();
    }

    private static void Override(Dictionary<string, string> statements) => statements["main"] = "DELETE FROM everything";

    /// Statements the type keeps in a dictionary of its own, filled where it is made.
    private static readonly Dictionary<string, string> Statements = new()
    {
        ["purge"] = "DELETE FROM sessions WHERE expired",
        ["vacuum"] = "VACUUM",
    };

    /// Through a dictionary held in a field, read at a constant key.
    public static int Purge(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Statements["purge"];
        return command.ExecuteNonQuery();
    }

    /// The caller's SQL, kept in a field's dictionary by one method and read by another: what it holds is the
    /// caller's, which is not known there, so the SQL stays dynamic.
    private static readonly Dictionary<string, string> Remembered = new();

    public static void Remember(string sql) => Remembered["last"] = sql;

    public static int RunRemembered(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Remembered["last"];
        return command.ExecuteNonQuery();
    }
}
