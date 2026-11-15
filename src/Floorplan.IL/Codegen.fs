/// Knowledge of how a particular compiler emits code (plan sections 3.5 and 6.7). The call-graph and taint
/// engines only see the neutral answers given here. One recognizer per language, selected per assembly.
namespace Floorplan.IL

type ICodegenRecognizer =
    abstract Name: string
    /// Whether this recognizer understands code in the assembly.
    abstract AppliesTo: assembly: AssemblyDef -> bool
    /// For a compiler-generated function object being constructed, the methods that run when it is invoked.
    /// The construction site is linked to them instead of resolving the later virtual invoke to every closure.
    abstract FunctionObjectTargets: set: AssemblySet * constructed: TypeDef -> MethodRef list
    /// Virtual invokes of function objects, which are resolved through construction sites only.
    abstract IsFunctionInvoke: method: MethodRef -> bool

/// Code generation every .NET compiler shares: delegates and async state machines.
module CommonCodegen =
    let asyncStateMachine = "System.Runtime.CompilerServices.IAsyncStateMachine"

    /// Delegate invocation is resolved through ldftn sites, never virtually.
    let isDelegateInvoke (set: AssemblySet) (m: MethodRef) =
        (m.Name = "Invoke" || m.Name = "BeginInvoke" || m.Name = "DynamicInvoke")
        && (set.BaseChain m.Type |> List.exists (fun t -> t.FullName = "System.MulticastDelegate" || t.FullName = "System.Delegate"))
