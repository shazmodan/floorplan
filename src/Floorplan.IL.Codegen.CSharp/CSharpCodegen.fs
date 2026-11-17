/// How the C# compiler emits lambdas, delegates and other generated code (plan sections 3.5 and 6.7). The
/// counterpart of the F# recognizer: everything C#-specific the analysis needs lives here. Most of C#'s output
/// is covered by what every compiler shares (delegates created with ldftn, async state machines, iterators, string
/// equality through System.String) or is made of platform calls the analysis reads anyway (string.Format,
/// DefaultInterpolatedStringHandler, string switches through String.op_Equality); this module adds what is
/// particular to Roslyn's output.
module Floorplan.IL.Codegen.CSharp.CSharpCodegen

open Floorplan.IL

/// The class Roslyn puts non-capturing lambdas in ("<>c"), with a singleton instance and a cached delegate per
/// lambda ("<>9__0_1").
let private isLambdaCacheClass (t: TypeRef) =
    let name = t.FullName
    name.EndsWith "/<>c" || name = "<>c"

/// The class Roslyn (C# 11 and later) puts cached delegates of static method groups in ("<>O", "<0>__Name").
let private isMethodGroupCacheClass (t: TypeRef) =
    let name = t.FullName
    name.EndsWith "/<>O" || name = "<>O"

/// A cached delegate: `ldsfld cache; dup; brtrue; pop; ... newobj; dup; stsfld cache`.
let isLazyCache (f: FieldRef) =
    (isLambdaCacheClass f.Type && f.Name.StartsWith "<>9__")
    || (isMethodGroupCacheClass f.Type && f.Name.StartsWith "<")

let recognizer =
    { new ICodegenRecognizer with
        member _.Name = "csharp"

        // Nothing marks an assembly as built by Roslyn, and the names recognized here are ones only Roslyn emits.
        member _.AppliesTo _ = true

        // C# lambdas are delegates, linked through their ldftn sites by the shared code.
        member _.FunctionObjectTargets(_, _) = []
        member _.IsFunctionInvoke _ = false

        // string.Format and interpolated strings are platform calls, read by the payload evaluator itself.
        member _.FormatArgument _ = None
        member _.FormatTemplate format = format
        member _.CollectionAccess _ = None

        member _.IsStringEquality m =
            m.Type.FullName = "System.String" && (m.Name = "op_Equality" || m.Name = "Equals") && m.ParameterCount >= 1

        member _.IsOpaque _ = false
        member _.IsLazyCache f = isLazyCache f }
