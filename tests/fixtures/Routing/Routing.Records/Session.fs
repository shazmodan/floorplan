module Routing.Records.Session

let show () = async { return "not authorized" }

let create (query: Map<string, string>) = async { return $"created {query.Count}" }

let remove (id: string) = async { return $"deleted {id}" }
