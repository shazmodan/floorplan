// Throwaway: dumps IL for every method of an assembly, plus PDB sequence points, and
// measures call-graph size over the assembly and every dependency DLL next to it.
open System
open System.IO
open System.Diagnostics
open Mono.Cecil
open Mono.Cecil.Cil

[<EntryPoint>]
let main argv =
    let path = argv[0]
    let filter = if argv.Length > 1 then argv[1] else ""
    let rp = ReaderParameters(ReadSymbols = true)
    let m = ModuleDefinition.ReadModule(path, rp)
    let rec allTypes (t: TypeDefinition) = seq { yield t; for n in t.NestedTypes do yield! allTypes n }
    for t in m.Types |> Seq.collect allTypes do
        for meth in t.Methods do
            if meth.HasBody && (filter = "" || (t.FullName + "::" + meth.Name).Contains filter) then
                printfn "=== %s::%s" t.FullName meth.Name
                for i in meth.Body.Instructions do
                    let sp = meth.DebugInformation.GetSequencePoint i
                    let loc = if isNull sp || sp.IsHidden then "" else sprintf "  // L%d" sp.StartLine
                    printfn "  %O%s" i loc
    if argv.Length > 2 then
        // scale probe: count methods and call edges across all DLLs in a directory
        let sw = Stopwatch.StartNew()
        let mutable methods = 0
        let mutable calls = 0
        for dll in Directory.GetFiles(argv[2], "*.dll") do
            try
                use md = ModuleDefinition.ReadModule(dll)
                for t in md.Types |> Seq.collect allTypes do
                    for meth in t.Methods do
                        methods <- methods + 1
                        if meth.HasBody then
                            for i in meth.Body.Instructions do
                                if i.OpCode.FlowControl = FlowControl.Call then calls <- calls + 1
            with _ -> ()
        printfn "SCALE: %d methods, %d call sites, %dms" methods calls sw.ElapsedMilliseconds
    0
