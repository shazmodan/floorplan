/// A test project: excluded from analysis by default, so this file access must not appear as an exit point.
module Shop.Tests.Tests

let readsFixture () = System.IO.File.ReadAllText "fixture.txt"
