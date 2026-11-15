/// How the F# compiler emits closures and other function objects (plan section 6.7). Everything F#-specific
/// that the call graph needs lives here, so a change in compiler output is fixed in one place.
module Floorplan.IL.Codegen.FSharp.FSharpCodegen

open Floorplan.IL

let private fsharpCore = "FSharp.Core"

/// Base types of compiler-generated function objects.
let functionObjectBases =
    set
        [ "Microsoft.FSharp.Core.FSharpFunc`2"
          "Microsoft.FSharp.Core.OptimizedClosures/FSharpFunc`3"
          "Microsoft.FSharp.Core.OptimizedClosures/FSharpFunc`4"
          "Microsoft.FSharp.Core.OptimizedClosures/FSharpFunc`5"
          "Microsoft.FSharp.Core.OptimizedClosures/FSharpFunc`6"
          // seq { } expressions compile to a subclass with GenerateNext.
          "Microsoft.FSharp.Core.CompilerServices.GeneratedSequenceBase`1" ]

/// Invoking a function value: resolved only through the construction site of the closure.
let isFunctionInvoke (m: MethodRef) =
    (m.Type.FullName = "Microsoft.FSharp.Core.FSharpFunc`2"
     || m.Type.FullName.StartsWith "Microsoft.FSharp.Core.OptimizedClosures/FSharpFunc`")
    && (m.Name = "Invoke" || m.Name = "InvokeFast")

let isFunctionObject (set: AssemblySet) (t: TypeDef) =
    set.BaseChain t.Ref |> List.exists (fun b -> functionObjectBases.Contains b.FullName)

let recognizer =
    { new ICodegenRecognizer with
        member _.Name = "fsharp"

        member _.AppliesTo assembly =
            assembly.Name = fsharpCore || assembly.ReferencedAssemblies |> List.contains fsharpCore

        member _.FunctionObjectTargets(set, constructed) =
            if isFunctionObject set constructed then
                constructed.Methods
                |> List.filter (fun m -> not m.IsConstructor && not m.IsStatic && not m.IsAbstract)
                |> List.map (fun m -> m.Ref)
            else
                []

        member _.IsFunctionInvoke m = isFunctionInvoke m }

// ---- Recognizers for values in F# compiler output, used by request-data tracking and route recovery ----

/// `list.Head` as emitted by pattern matching.
let isListHead (m: MethodRef) =
    m.Type.FullName = "Microsoft.FSharp.Collections.FSharpList`1"
    && (m.Name = "get_HeadOrDefault" || m.Name = "get_Head")

/// `list.Tail` as emitted by pattern matching.
let isListTail (m: MethodRef) =
    m.Type.FullName = "Microsoft.FSharp.Collections.FSharpList`1"
    && (m.Name = "get_TailOrNull" || m.Name = "get_Tail")

/// `Some x` unwrapped by pattern matching.
let isOptionValue (m: MethodRef) =
    (m.Type.FullName = "Microsoft.FSharp.Core.FSharpOption`1" || m.Type.FullName = "Microsoft.FSharp.Core.FSharpValueOption`1")
    && m.Name = "get_Value"

/// Conversion of an array or sequence into an F# list, which keeps the elements and their order.
let isToList (m: MethodRef) =
    (m.Type.FullName = "Microsoft.FSharp.Collections.ListModule" && (m.Name = "OfArray" || m.Name = "OfSeq"))
    || (m.Type.FullName = "Microsoft.FSharp.Collections.SeqModule" && m.Name = "ToList")
    || (m.Type.FullName = "Microsoft.FSharp.Collections.ArrayModule" && m.Name = "ToList")

/// String equality as F# emits it for `=` on strings and for string patterns.
let isStringEquality (m: MethodRef) =
    (m.Type.FullName = "System.String" && (m.Name = "Equals" || m.Name = "op_Equality") && m.ParameterCount >= 1)
    || (m.Type.FullName = "Microsoft.FSharp.Core.LanguagePrimitives/HashCompare"
        && (m.Name = "GenericEqualityIntrinsic" || m.Name = "GenericEqualityERIntrinsic"))
    || (m.Type.FullName = "Microsoft.FSharp.Core.Operators" && m.Name = "op_Equality")

/// The format string of an F# interpolated or printf-style string, with each hole shown as {…}.
let formatTemplate (format: string) =
    System.Text.RegularExpressions.Regex.Replace(format, @"%(P\(\)|[-+0 #]*\d*(\.\d+)?[a-zA-Z])", "{…}").Replace("%%", "%")

/// Builders of formatted strings: `new PrintfFormat<...>(format, ...)`.
let isPrintfFormatConstructor (m: MethodRef) =
    m.Name = ".ctor" && m.Type.FullName.StartsWith "Microsoft.FSharp.Core.PrintfFormat`"
