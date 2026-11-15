/// A language-neutral view of compiled code. Nothing downstream of the reader sees Mono.Cecil, so the
/// library can be swapped (plan section 3.3 and open decision 2).
namespace Floorplan.IL

open System

/// A type, by the assembly a reference names and full name. Nested types use '/', generic types keep their
/// arity suffix ("System.Collections.Generic.List`1"). Generic arguments are not part of a TypeRef. For framework
/// types the assembly may be a facade (netstandard, System.Runtime); AssemblySet follows forwarders.
type TypeRef =
    { Assembly: string
      FullName: string }

    override t.ToString() = t.FullName

    /// The simple name, without namespace, enclosing types or generic arity.
    member t.Name =
        let n = t.FullName.Split([| '.'; '/' |]) |> Array.last
        match n.IndexOf '`' with
        | -1 -> n
        | i -> n.Substring(0, i)

    member t.Namespace =
        let outer = t.FullName.Split('/').[0]
        match outer.LastIndexOf '.' with
        | -1 -> ""
        | i -> outer.Substring(0, i)

/// A method, identified by Key: "[Assembly]Declaring.Type::Name`arity(param,param)". Parameter types are
/// normalized so that a call site and the definition it binds to produce the same key. LocalKey is the key
/// without the assembly.
[<CustomEquality; CustomComparison>]
type MethodRef =
    { Type: TypeRef
      Name: string
      Key: string
      LocalKey: string
      ParameterTypes: string list
      ReturnType: string
      HasThis: bool
      GenericArity: int
      /// For a call to a generic method instance, its type arguments (e.g. T of UseMiddleware<T>). Not part of
      /// the identity.
      TypeArguments: TypeRef list }

    override m.Equals other =
        match other with
        | :? MethodRef as o -> m.Key = o.Key
        | _ -> false

    override m.GetHashCode() = m.Key.GetHashCode()

    interface IComparable with
        member m.CompareTo other =
            match other with
            | :? MethodRef as o -> String.CompareOrdinal(m.Key, o.Key)
            | _ -> 1

    override m.ToString() = m.Display
    member m.ParameterCount = m.ParameterTypes.Length
    member m.ReturnsValue = m.ReturnType <> "System.Void"
    /// "Declaring.Type::Name", for people.
    member m.Display = m.Type.FullName + "::" + m.Name

type FieldRef =
    { Type: TypeRef
      Name: string
      FieldType: string }

    member f.Key = f.Type.FullName + "::" + f.Name

type SourcePoint =
    { File: string
      StartLine: int
      StartColumn: int
      EndLine: int
      EndColumn: int }

type Comparison =
    | Eq
    | NotEq
    | Lt
    | Gt
    | Le
    | Ge

/// Instructions, normalized: every short and long form of an opcode maps to one case.
type Op =
    | Nop
    | LdArg of int
    | LdArgA of int
    | StArg of int
    | LdLoc of int
    | LdLocA of int
    | StLoc of int
    | LdStr of string
    | LdcI4 of int
    | LdcI8 of int64
    | LdcR of float
    | LdNull
    | LdFld of FieldRef
    | LdFldA of FieldRef
    | StFld of FieldRef
    | LdSFld of FieldRef
    | LdSFldA of FieldRef
    | StSFld of FieldRef
    | Call of MethodRef
    | CallVirt of MethodRef
    | NewObj of MethodRef
    | LdFtn of MethodRef
    | LdVirtFtn of MethodRef
    /// The `constrained.` prefix of the next callvirt.
    | Constrained of TypeRef
    | InitObj of TypeRef
    | Box of TypeRef
    | UnboxAny of TypeRef
    | CastClass of TypeRef
    | IsInst of TypeRef
    | LdToken
    | Br of int
    | BrTrue of int
    | BrFalse of int
    /// beq, bne.un, blt, ... : pops two, branches.
    | BrCmp of Comparison * int
    | Switch of int list
    | Leave of int
    | Ceq
    | Cgt
    | Clt
    | Ret
    | Throw
    | Rethrow
    | EndFinally
    | Dup
    | Pop
    | LdLen
    | LdElem
    | StElem
    | NewArr of TypeRef
    /// Anything else, with its stack effect.
    | Other of name: string * pops: int * pushes: int

type Instruction =
    { Offset: int
      Op: Op
      /// The sequence point that starts at this instruction, if any.
      Point: SourcePoint option }

type HandlerKind =
    | Catch
    | Filter
    | Finally
    | Fault

type ExceptionHandler =
    { TryStart: int
      TryEnd: int
      HandlerStart: int
      HandlerEnd: int
      FilterStart: int option
      Kind: HandlerKind }

type MethodBody =
    { Instructions: Instruction[]
      Handlers: ExceptionHandler list
      LocalTypes: TypeRef list }

type MethodDef =
    { Ref: MethodRef
      IsStatic: bool
      IsVirtual: bool
      IsAbstract: bool
      IsFinal: bool
      IsConstructor: bool
      IsCompilerGenerated: bool
      IsPublic: bool
      /// Parameter names as declared (empty strings where the metadata has none).
      ParameterNames: string list
      /// The native library, for a P/Invoke declaration.
      PInvokeModule: string option
      /// Methods this one explicitly overrides (.override), e.g. explicit interface implementations.
      Overrides: MethodRef list
      Body: Lazy<MethodBody option> }

type TypeDef =
    { Ref: TypeRef
      BaseType: TypeRef option
      Interfaces: TypeRef list
      IsInterface: bool
      IsAbstract: bool
      IsSealed: bool
      IsValueType: bool
      DeclaringType: TypeRef option
      IsCompilerGenerated: bool
      Methods: MethodDef list
      /// Constant static fields with their values (enum members, literals); integers as int64.
      Literals: (string * obj) list }

/// Where an assembly comes from, which decides how deep the analysis goes (plan sections 3.2 and 4.3).
type AssemblyOrigin =
    /// The output of a project in the solution.
    | User of projectId: string
    /// A NuGet or other dependency: analyzed, and summarized.
    | Dependency
    /// A shared framework (Microsoft.NETCore.App, Microsoft.AspNetCore.App): read for metadata only.
    | Framework

type AssemblyDef =
    { Name: string
      Path: string
      Mvid: Guid
      TargetFramework: string option
      Types: TypeDef list
      ReferencedAssemblies: string list
      /// Type full name to the assembly it is forwarded to.
      Forwarders: Map<string, string>
      HasSymbols: bool
      /// The method the runtime starts, for executables.
      EntryPoint: MethodRef option }

type IAssemblyReader =
    /// Reads metadata. Bodies are read on demand. With symbols, PDB sequence points are attached.
    abstract Read: path: string * readSymbols: bool -> AssemblyDef
    inherit IDisposable

module MethodRef =
    /// Whether the method is a constructor or static constructor.
    let isConstructor (m: MethodRef) = m.Name = ".ctor" || m.Name = ".cctor"

module Instruction =
    /// The nearest sequence point at or before the instruction at `index`.
    let pointAt (instructions: Instruction[]) (index: int) =
        let mutable i = index
        let mutable result = None

        while result.IsNone && i >= 0 do
            result <- instructions[i].Point
            i <- i - 1

        result

    let indexOfOffset (instructions: Instruction[]) (offset: int) =
        Array.BinarySearch(instructions |> Array.map (fun i -> i.Offset), offset)
