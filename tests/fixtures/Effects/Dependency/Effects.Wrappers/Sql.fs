/// A wrapper over ADO.NET written in F#: SQL from the caller reaches a command through F#'s own compiled forms.
module Effects.Wrappers.Sql

open System.Data.Common

/// SQL kept in a record the caller makes.
type Query = { Text: string }

/// The argument is the command text.
let exec (connection: DbConnection) (sql: string) =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery()

/// Kept in a record field.
let execute (connection: DbConnection) (query: Query) =
    use command = connection.CreateCommand()
    command.CommandText <- query.Text
    command.ExecuteNonQuery()

/// A task expression.
let execTask (connection: DbConnection) (sql: string) =
    task {
        use command = connection.CreateCommand()
        command.CommandText <- sql
        return! command.ExecuteNonQueryAsync()
    }

/// A sequence expression.
let rows (connection: DbConnection) (sql: string) =
    seq {
        use command = connection.CreateCommand()
        command.CommandText <- sql
        use reader = command.ExecuteReader()

        while reader.Read() do
            yield reader.GetValue 0
    }
