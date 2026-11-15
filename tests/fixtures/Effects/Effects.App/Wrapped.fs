/// Effects through wrapper libraries: the call site is in user code, the primitive inside a dependency.
module Effects.Wrapped

open Dapper
open Flurl.Http
open Microsoft.Data.Sqlite

let queryNames () =
    use connection = new SqliteConnection("Data Source=app.db")
    connection.Query<string>("SELECT name FROM users") |> Seq.toList

let fetchUsers () =
    "https://api.example.org/users".GetStringAsync().Result
