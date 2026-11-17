/// Effects behind an F# wrapper library (Dependency/Effects.Wrappers), whose payloads come from these call sites.
module Effects.ThroughWrappers

open Microsoft.Data.Sqlite
open Effects.Wrappers

let private connect () = new SqliteConnection("Data Source=app.db")

let execLiteral () = Sql.exec (connect ()) "DELETE FROM sessions"

let executeRecord () = Sql.execute (connect ()) { Text = "UPDATE orders SET seen = 1" }

let execTaskLiteral () = (Sql.execTask (connect ()) "DELETE FROM tokens").Result

let rowsLiteral () = Sql.rows (connect ()) "SELECT name FROM users" |> Seq.toList
