/// ADO.NET through two different providers. Neither is named in the catalog: both derive from DbCommand.
module Effects.Database

open Microsoft.Data.Sqlite
open Npgsql

let sqlite () =
    use connection = new SqliteConnection("Data Source=app.db")
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- "DELETE FROM sessions"
    command.ExecuteNonQuery() |> ignore

let postgres () =
    use connection = new NpgsqlConnection("Host=localhost")
    connection.Open()
    use command = new NpgsqlCommand("SELECT count(*) FROM orders", connection)
    command.ExecuteScalar()
