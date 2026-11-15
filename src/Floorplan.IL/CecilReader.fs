/// IAssemblyReader over Mono.Cecil. Assemblies are read as metadata and never loaded for execution.
module Floorplan.IL.CecilReader

open System
open System.Collections.Generic
open System.IO
open Mono.Cecil
open Mono.Cecil.Cil

let rec private scopeAssembly (fallback: string) (t: TypeReference) =
    if not (isNull t.DeclaringType) then
        scopeAssembly fallback t.DeclaringType
    else
        match t.Scope with
        | :? AssemblyNameReference as a -> a.Name
        | :? ModuleDefinition as m -> m.Assembly.Name.Name
        | _ -> fallback

/// A type's identity without generic arguments, modifiers or by-ref/array wrappers stripped of meaning.
let rec private elementType (t: TypeReference) : TypeReference =
    match t with
    | :? GenericInstanceType as g -> elementType g.ElementType
    | :? RequiredModifierType as r -> elementType r.ElementType
    | :? OptionalModifierType as o -> elementType o.ElementType
    | :? PinnedType as p -> elementType p.ElementType
    | _ -> t

let typeRef (fallback: string) (t: TypeReference) : TypeRef =
    let e = elementType t

    { Assembly = scopeAssembly fallback e
      FullName = e.FullName }

/// A normalized spelling of a type in a signature: generic parameters by position, so that "T" in a
/// definition and "!0" in a member reference agree.
let rec typeKey (t: TypeReference) : string =
    match t with
    | :? GenericParameter as p ->
        (if p.Type = GenericParameterType.Type then "!" else "!!") + string p.Position
    | :? GenericInstanceType as g ->
        typeKey g.ElementType + "<" + (g.GenericArguments |> Seq.map typeKey |> String.concat ",") + ">"
    | :? ByReferenceType as r -> typeKey r.ElementType + "&"
    | :? PointerType as p -> typeKey p.ElementType + "*"
    | :? ArrayType as a -> typeKey a.ElementType + "[" + String(',', a.Rank - 1) + "]"
    | :? RequiredModifierType as r -> typeKey r.ElementType
    | :? OptionalModifierType as o -> typeKey o.ElementType
    | :? PinnedType as p -> typeKey p.ElementType
    | :? SentinelType as s -> typeKey s.ElementType
    | _ -> t.FullName

let methodRef (fallback: string) (m: MethodReference) : MethodRef =
    let typeArguments =
        match m with
        | :? GenericInstanceMethod as g -> g.GenericArguments |> Seq.map (typeRef fallback) |> Seq.toList
        | _ -> []

    // For a generic method instance, the generic definition; parameters of members of generic types are
    // already in open form in metadata.
    let m = m.GetElementMethod()
    let declaring = typeRef fallback m.DeclaringType
    let parameters = m.Parameters |> Seq.map (fun p -> typeKey p.ParameterType) |> Seq.toList
    let arity = m.GenericParameters.Count

    let localKey = $"{declaring.FullName}::{m.Name}`{arity}({String.Join(',', parameters)})"

    { Type = declaring
      Name = m.Name
      Key = $"[{declaring.Assembly}]{localKey}"
      LocalKey = localKey
      ParameterTypes = parameters
      ReturnType = typeKey m.ReturnType
      HasThis = m.HasThis
      GenericArity = arity
      TypeArguments = typeArguments }

let fieldRef (fallback: string) (f: FieldReference) : FieldRef =
    { Type = typeRef fallback f.DeclaringType
      Name = f.Name
      FieldType = typeKey f.FieldType }

let private comparison (code: Code) =
    match code with
    | Code.Beq
    | Code.Beq_S -> Some Eq
    | Code.Bne_Un
    | Code.Bne_Un_S -> Some NotEq
    | Code.Blt
    | Code.Blt_S
    | Code.Blt_Un
    | Code.Blt_Un_S -> Some Lt
    | Code.Bgt
    | Code.Bgt_S
    | Code.Bgt_Un
    | Code.Bgt_Un_S -> Some Gt
    | Code.Ble
    | Code.Ble_S
    | Code.Ble_Un
    | Code.Ble_Un_S -> Some Le
    | Code.Bge
    | Code.Bge_S
    | Code.Bge_Un
    | Code.Bge_Un_S -> Some Ge
    | _ -> None

let private stackCount (behaviour: StackBehaviour) =
    match behaviour with
    | StackBehaviour.Pop0
    | StackBehaviour.Push0 -> 0
    | StackBehaviour.Pop1
    | StackBehaviour.Popi
    | StackBehaviour.Popref
    | StackBehaviour.Push1
    | StackBehaviour.Pushi
    | StackBehaviour.Pushi8
    | StackBehaviour.Pushr4
    | StackBehaviour.Pushr8
    | StackBehaviour.Pushref -> 1
    | StackBehaviour.Pop1_pop1
    | StackBehaviour.Popi_pop1
    | StackBehaviour.Popi_popi
    | StackBehaviour.Popi_popi8
    | StackBehaviour.Popi_popr4
    | StackBehaviour.Popi_popr8
    | StackBehaviour.Popref_pop1
    | StackBehaviour.Popref_popi
    | StackBehaviour.Push1_push1 -> 2
    | StackBehaviour.Popi_popi_popi
    | StackBehaviour.Popref_popi_popi
    | StackBehaviour.Popref_popi_popi8
    | StackBehaviour.Popref_popi_popr4
    | StackBehaviour.Popref_popi_popr8
    | StackBehaviour.Popref_popi_popref -> 3
    | _ -> 0

let private convert (asm: string) (method: MethodDefinition) (i: Mono.Cecil.Cil.Instruction) : Op =
    let target () = (i.Operand :?> Mono.Cecil.Cil.Instruction).Offset
    let argIndex (p: ParameterDefinition) = if method.HasThis then p.Index + 1 else p.Index

    let arg () =
        match i.Operand with
        | :? ParameterDefinition as p -> argIndex p
        | _ -> 0

    let local () =
        match i.Operand with
        | :? VariableDefinition as v -> v.Index
        | _ -> 0

    let mref () = methodRef asm (i.Operand :?> MethodReference)
    let fref () = fieldRef asm (i.Operand :?> FieldReference)
    let tref () = typeRef asm (i.Operand :?> TypeReference)

    match i.OpCode.Code with
    | Code.Nop -> Nop
    | Code.Ldarg_0 -> LdArg 0
    | Code.Ldarg_1 -> LdArg 1
    | Code.Ldarg_2 -> LdArg 2
    | Code.Ldarg_3 -> LdArg 3
    | Code.Ldarg
    | Code.Ldarg_S -> LdArg(arg ())
    | Code.Ldarga
    | Code.Ldarga_S -> LdArgA(arg ())
    | Code.Starg
    | Code.Starg_S -> StArg(arg ())
    | Code.Ldloc_0 -> LdLoc 0
    | Code.Ldloc_1 -> LdLoc 1
    | Code.Ldloc_2 -> LdLoc 2
    | Code.Ldloc_3 -> LdLoc 3
    | Code.Ldloc
    | Code.Ldloc_S -> LdLoc(local ())
    | Code.Ldloca
    | Code.Ldloca_S -> LdLocA(local ())
    | Code.Stloc_0 -> StLoc 0
    | Code.Stloc_1 -> StLoc 1
    | Code.Stloc_2 -> StLoc 2
    | Code.Stloc_3 -> StLoc 3
    | Code.Stloc
    | Code.Stloc_S -> StLoc(local ())
    | Code.Ldstr -> LdStr(i.Operand :?> string)
    | Code.Ldc_I4_M1 -> LdcI4 -1
    | Code.Ldc_I4_0 -> LdcI4 0
    | Code.Ldc_I4_1 -> LdcI4 1
    | Code.Ldc_I4_2 -> LdcI4 2
    | Code.Ldc_I4_3 -> LdcI4 3
    | Code.Ldc_I4_4 -> LdcI4 4
    | Code.Ldc_I4_5 -> LdcI4 5
    | Code.Ldc_I4_6 -> LdcI4 6
    | Code.Ldc_I4_7 -> LdcI4 7
    | Code.Ldc_I4_8 -> LdcI4 8
    | Code.Ldc_I4_S -> LdcI4(int (i.Operand :?> sbyte))
    | Code.Ldc_I4 -> LdcI4(i.Operand :?> int)
    | Code.Ldc_I8 -> LdcI8(i.Operand :?> int64)
    | Code.Ldc_R4 -> LdcR(float (i.Operand :?> float32))
    | Code.Ldc_R8 -> LdcR(i.Operand :?> float)
    | Code.Ldnull -> LdNull
    | Code.Ldfld -> LdFld(fref ())
    | Code.Ldflda -> LdFldA(fref ())
    | Code.Stfld -> StFld(fref ())
    | Code.Ldsfld -> LdSFld(fref ())
    | Code.Ldsflda -> LdSFldA(fref ())
    | Code.Stsfld -> StSFld(fref ())
    | Code.Call -> Call(mref ())
    | Code.Callvirt -> CallVirt(mref ())
    | Code.Newobj -> NewObj(mref ())
    | Code.Ldftn -> LdFtn(mref ())
    | Code.Ldvirtftn -> LdVirtFtn(mref ())
    | Code.Constrained -> Constrained(tref ())
    | Code.Initobj -> InitObj(tref ())
    | Code.Box -> Box(tref ())
    | Code.Unbox_Any -> UnboxAny(tref ())
    | Code.Castclass -> CastClass(tref ())
    | Code.Isinst -> IsInst(tref ())
    | Code.Ldtoken -> LdToken
    | Code.Br
    | Code.Br_S -> Br(target ())
    | Code.Brtrue
    | Code.Brtrue_S -> BrTrue(target ())
    | Code.Brfalse
    | Code.Brfalse_S -> BrFalse(target ())
    | Code.Leave
    | Code.Leave_S -> Leave(target ())
    | Code.Switch ->
        Switch(i.Operand :?> Mono.Cecil.Cil.Instruction[] |> Array.map (fun t -> t.Offset) |> Array.toList)
    | Code.Ceq -> Ceq
    | Code.Cgt
    | Code.Cgt_Un -> Cgt
    | Code.Clt
    | Code.Clt_Un -> Clt
    | Code.Ret -> Ret
    | Code.Throw -> Throw
    | Code.Rethrow -> Rethrow
    | Code.Endfinally -> EndFinally
    | Code.Dup -> Dup
    | Code.Pop -> Pop
    | Code.Ldlen -> LdLen
    | Code.Newarr -> NewArr(tref ())
    | c when (string c).StartsWith "Ldelem" -> LdElem
    | c when (string c).StartsWith "Stelem" -> StElem
    | c ->
        match comparison c with
        | Some cmp -> BrCmp(cmp, target ())
        | None ->
            let pops =
                if i.OpCode.StackBehaviourPop = StackBehaviour.Varpop then
                    // calli: the signature decides; treat as unknown arity 0 (rare in analyzed code).
                    0
                else
                    stackCount i.OpCode.StackBehaviourPop

            let pushes =
                if i.OpCode.StackBehaviourPush = StackBehaviour.Varpush then 1 else stackCount i.OpCode.StackBehaviourPush

            Other(string c, pops, pushes)

let private isHidden (sp: SequencePoint) = sp.IsHidden || sp.StartLine = 0xfeefee

let private readBody (asm: string) (method: MethodDefinition) : Floorplan.IL.MethodBody option =
    if not method.HasBody then
        None
    else
        try
            let body = method.Body
            let debug = method.DebugInformation

            let points =
                if not (isNull debug) && debug.HasSequencePoints then
                    debug.SequencePoints
                    |> Seq.filter (fun sp -> not (isHidden sp))
                    |> Seq.map (fun sp ->
                        sp.Offset,
                        { File = sp.Document.Url
                          StartLine = sp.StartLine
                          StartColumn = sp.StartColumn - 1
                          EndLine = sp.EndLine
                          EndColumn = sp.EndColumn - 1 })
                    |> dict
                else
                    dict []

            let instructions =
                body.Instructions
                |> Seq.map (fun i ->
                    { Offset = i.Offset
                      Op = convert asm method i
                      Point =
                        match points.TryGetValue i.Offset with
                        | true, p -> Some p
                        | _ -> None })
                |> Seq.toArray

            let handlers =
                body.ExceptionHandlers
                |> Seq.map (fun h ->
                    { TryStart = h.TryStart.Offset
                      TryEnd = if isNull h.TryEnd then Int32.MaxValue else h.TryEnd.Offset
                      HandlerStart = h.HandlerStart.Offset
                      HandlerEnd = if isNull h.HandlerEnd then Int32.MaxValue else h.HandlerEnd.Offset
                      FilterStart = if isNull h.FilterStart then None else Some h.FilterStart.Offset
                      Kind =
                        match h.HandlerType with
                        | ExceptionHandlerType.Catch -> Catch
                        | ExceptionHandlerType.Filter -> Filter
                        | ExceptionHandlerType.Finally -> Finally
                        | _ -> Fault })
                |> Seq.toList

            Some
                { Instructions = instructions
                  Handlers = handlers
                  LocalTypes = body.Variables |> Seq.map (fun v -> typeRef asm v.VariableType) |> Seq.toList }
        with _ ->
            // Obfuscated or malformed bodies are skipped, not fatal.
            None

let private hasAttribute (name: string) (attributes: Mono.Collections.Generic.Collection<CustomAttribute>) =
    attributes |> Seq.exists (fun a -> a.AttributeType.Name = name)

let rec private attributeValue (v: obj) : AttributeValue =
    match v with
    | :? string as s -> AttributeValue.Text s
    | :? bool as b -> AttributeValue.Flag b
    | :? CustomAttributeArgument as a -> attributeValue a.Value
    | :? (CustomAttributeArgument[]) as items -> AttributeValue.Many(items |> Array.map (fun a -> attributeValue a.Value) |> Array.toList)
    | null -> AttributeValue.Other
    | :? int as n -> AttributeValue.Number(int64 n)
    | :? int64 as n -> AttributeValue.Number n
    | :? int16 as n -> AttributeValue.Number(int64 n)
    | :? byte as n -> AttributeValue.Number(int64 n)
    | :? uint32 as n -> AttributeValue.Number(int64 n)
    | _ -> AttributeValue.Other

let private readAttributes (asm: string) (attributes: Mono.Collections.Generic.Collection<CustomAttribute>) : AttributeDef list =
    attributes
    |> Seq.choose (fun a ->
        try
            Some
                { Type = typeRef asm a.AttributeType
                  Arguments = a.ConstructorArguments |> Seq.map (fun x -> attributeValue x.Value) |> Seq.toList
                  Named =
                    Seq.append a.Properties a.Fields
                    |> Seq.map (fun n -> n.Name, attributeValue n.Argument.Value)
                    |> Seq.toList }
        with _ ->
            // An attribute whose blob cannot be read (a type in an assembly that is not there) is left out.
            None)
    |> Seq.toList

let private readMethod (asm: string) (m: MethodDefinition) : MethodDef =
    { Ref = methodRef asm m
      IsStatic = m.IsStatic
      IsVirtual = m.IsVirtual
      IsAbstract = m.IsAbstract
      IsFinal = m.IsFinal
      IsConstructor = m.IsConstructor
      IsCompilerGenerated =
        hasAttribute "CompilerGeneratedAttribute" m.CustomAttributes
        || m.Name.Contains '@'
        || m.Name.StartsWith "<"
      IsPublic = m.IsPublic
      IsSpecialName = m.IsSpecialName
      Attributes = readAttributes asm m.CustomAttributes
      ParameterNames = m.Parameters |> Seq.map (fun p -> if isNull p.Name then "" else p.Name) |> Seq.toList
      PInvokeModule =
        if m.IsPInvokeImpl && not (isNull m.PInvokeInfo) then
            Some(if isNull m.PInvokeInfo.Module then "" else m.PInvokeInfo.Module.Name)
        else
            None
      Overrides = m.Overrides |> Seq.map (methodRef asm) |> Seq.toList
      Body = lazy (readBody asm m) }

let rec private allTypes (t: TypeDefinition) =
    seq {
        yield t

        for n in t.NestedTypes do
            yield! allTypes n
    }

let private readType (asm: string) (t: TypeDefinition) : TypeDef =
    { Ref = typeRef asm t
      BaseType = if isNull t.BaseType then None else Some(typeRef asm t.BaseType)
      Interfaces = t.Interfaces |> Seq.map (fun i -> typeRef asm i.InterfaceType) |> Seq.toList
      IsInterface = t.IsInterface
      IsAbstract = t.IsAbstract
      IsSealed = t.IsSealed
      IsValueType = t.IsValueType
      DeclaringType = if isNull t.DeclaringType then None else Some(typeRef asm t.DeclaringType)
      IsCompilerGenerated =
        hasAttribute "CompilerGeneratedAttribute" t.CustomAttributes
        || t.Name.Contains '@'
        || t.Name.StartsWith "<"
      IsPublic = t.IsPublic || t.IsNestedPublic
      IsGeneric = t.HasGenericParameters
      Attributes = readAttributes asm t.CustomAttributes
      Methods = t.Methods |> Seq.map (readMethod asm) |> Seq.toList
      Literals =
        t.Fields
        |> Seq.filter (fun f -> f.IsStatic && f.HasConstant && not (isNull f.Constant))
        |> Seq.map (fun f ->
            let value: obj =
                match f.Constant with
                | :? string as s -> box s
                | :? bool as b -> box (if b then 1L else 0L)
                | :? char as c -> box (int64 c)
                | :? float as d -> box d
                | :? float32 as d -> box (float d)
                | other ->
                    try box (Convert.ToInt64 other) with _ -> other

            f.Name, value)
        |> Seq.toList }

let private targetFramework (a: AssemblyDefinition) =
    a.CustomAttributes
    |> Seq.tryFind (fun c -> c.AttributeType.FullName = "System.Runtime.Versioning.TargetFrameworkAttribute")
    |> Option.bind (fun c ->
        if c.ConstructorArguments.Count > 0 then
            Some(string c.ConstructorArguments[0].Value)
        else
            None)

/// Reads assemblies with Cecil. Modules stay open so method bodies can be read lazily; dispose to release.
type CecilAssemblyReader() =
    let opened = List<ModuleDefinition>()

    interface IAssemblyReader with
        member _.Read(path, readSymbols) =
            let pdbExists = File.Exists(Path.ChangeExtension(path, ".pdb"))

            let parameters =
                ReaderParameters(ReadingMode = ReadingMode.Deferred, ReadSymbols = (readSymbols && pdbExists), InMemory = true)

            let md =
                try
                    ModuleDefinition.ReadModule(path, parameters)
                with _ when parameters.ReadSymbols ->
                    // A PDB that does not match is not fatal: read without it.
                    ModuleDefinition.ReadModule(path, ReaderParameters(ReadingMode = ReadingMode.Deferred, InMemory = true))

            lock opened (fun () -> opened.Add md)
            let asm = md.Assembly.Name.Name

            { Name = asm
              Path = path
              Mvid = md.Mvid
              TargetFramework = targetFramework md.Assembly
              Types = md.Types |> Seq.collect allTypes |> Seq.map (readType asm) |> Seq.toList
              ReferencedAssemblies = md.AssemblyReferences |> Seq.map (fun r -> r.Name) |> Seq.toList
              Forwarders =
                md.ExportedTypes
                |> Seq.filter (fun e -> e.IsForwarder)
                |> Seq.choose (fun e ->
                    match e.Scope with
                    | :? AssemblyNameReference as a -> Some(e.FullName, a.Name)
                    | _ -> None)
                |> Map.ofSeq
              HasSymbols = md.HasSymbols
              EntryPoint = if isNull md.EntryPoint then None else Some(methodRef asm md.EntryPoint) }

        member _.Dispose() =
            lock opened (fun () ->
                for m in opened do
                    m.Dispose()

                opened.Clear())

let create () = new CecilAssemblyReader() :> IAssemblyReader
