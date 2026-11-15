/// Code the symbolic interpreter runs in SymbolicTests: each function exercises one model, with constant inputs
/// or request data. Kept in its own module so the tests can find the compiled methods by name.
module Floorplan.Tests.SymbolicSamples

open System.Collections.Generic
open System.Reflection
open System.Text.RegularExpressions

/// F#'s dict boxes value-type keys (here chars) in a StructBox.
let dictOfChars () =
    let d = dict [ 'a', "first"; 'b', "second" ]
    d['b']

let dictionaryOfStrings () =
    let d = Dictionary<string, int>()
    d["k"] <- 3
    d.Add("j", 4)
    if d.ContainsKey "k" then d["j"] else 0

let charText () =
    let c = 'q'
    c.ToString()

/// A loop with a known end, longer than the loop allowance of paths that forked.
let listOfLongString () =
    List.ofSeq "abcdefghijklmnopqrstuvwxyz" |> List.length

let interpolated (name: string) (count: int) = $"hello %s{name}, %d{count}"

let replaceWithEvaluator () =
    Regex(@"\{(\w+)\}").Replace("/a/{x}/{y}", MatchEvaluator(fun m -> "<" + m.Groups[1].Value + ">"))

/// The group count of a match on request data carries the match condition.
let groupCount (path: string) =
    Regex.Match(path, @"^/orders/(\d+)$").Groups.Count > 1

let handler (orderId: int) = orderId + orderId

let firstParameterName (f: int -> int) =
    let ps = (f.GetType().GetMethods()[0]).GetParameters()
    ps[0].Name

let parameterNameOfHandler () = firstParameterName handler

let invokeReflected (f: int -> int) =
    let invoker = MethodInvoker.Create((f.GetType().GetMethods()[0]))
    invoker.Invoke(f, box 5) :?> int

let invokeHandlerByReflection () = invokeReflected handler
