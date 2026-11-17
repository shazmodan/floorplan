/// Knowledge of how a particular compiler emits code (plan sections 3.5 and 6.7). The call-graph and taint
/// engines only see the neutral answers given here. One recognizer per language, selected per assembly.
namespace Floorplan.IL

/// What a call does to a sequence, in terms route recovery understands.
type CollectionAccess =
    /// The first element.
    | Head
    /// The rest after the first element; null when the sequence is empty.
    | Tail
    /// The same elements in the same order, in another collection type.
    | SameElements
    /// The number of elements.
    | Count

type ICodegenRecognizer =
    abstract Name: string
    /// Whether this recognizer understands code in the assembly.
    abstract AppliesTo: assembly: AssemblyDef -> bool
    /// For a compiler-generated function object being constructed, the methods that run when it is invoked.
    /// The construction site is linked to them instead of resolving the later virtual invoke to every closure.
    abstract FunctionObjectTargets: set: AssemblySet * constructed: TypeDef -> MethodRef list
    /// Virtual invokes of function objects, which are resolved through construction sites only.
    abstract IsFunctionInvoke: method: MethodRef -> bool
    /// For a call that builds a string from a format literal, the index of the argument holding the literal.
    abstract FormatArgument: method: MethodRef -> int option
    /// A format literal rendered as a template, with each hole shown as {…}.
    abstract FormatTemplate: format: string -> string
    /// How a call in this language's emitted code accesses a sequence, if it does.
    abstract CollectionAccess: method: MethodRef -> CollectionAccess option
    /// Whether a call compares two strings for equality, as this compiler emits it.
    abstract IsStringEquality: method: MethodRef -> bool
    /// Runtime-library machinery that never decides routing (generic hashing and comparison, formatting,
    /// reflection). A symbolic run treats calls into it as returning unknown values instead of interpreting it.
    abstract IsOpaque: method: MethodRef -> bool
    /// A static field the compiler fills on first use and tests for null before (a cached delegate): null until
    /// code stores it, whatever static initialization runs.
    abstract IsLazyCache: field: FieldRef -> bool
    /// For a field of a state machine (async method, iterator) that holds one of the starting method's
    /// arguments, the other fields the starting method may store that argument into (C# iterators store it into
    /// `<>3__x`, and the enumerator copies it into `x`).
    abstract StateMachineArgumentFields: field: string -> string list

/// Code generation every .NET compiler shares: delegates and async state machines.
module CommonCodegen =
    let asyncStateMachine = "System.Runtime.CompilerServices.IAsyncStateMachine"

    /// Enumerators: constructing one implies it will be enumerated, so construction links to MoveNext.
    let enumeratorInterfaces =
        set [ "System.Collections.IEnumerator"; "System.Collections.Generic.IAsyncEnumerator`1" ]

    let enumeratorMethods = set [ "MoveNext"; "MoveNextAsync"; "GetEnumerator"; "GetAsyncEnumerator" ]

    /// Delegate invocation is resolved through ldftn sites, never virtually.
    let isDelegateInvoke (set: AssemblySet) (m: MethodRef) =
        (m.Name = "Invoke" || m.Name = "BeginInvoke" || m.Name = "DynamicInvoke")
        && (set.BaseChain m.Type |> List.exists (fun t -> t.FullName = "System.MulticastDelegate" || t.FullName = "System.Delegate"))
