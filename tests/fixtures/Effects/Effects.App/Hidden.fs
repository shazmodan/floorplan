/// Categories hidden by default, native calls and blind spots.
module Effects.Hidden

open System
open System.Runtime.InteropServices

let now () = DateTime.Now

let home () = Environment.GetEnvironmentVariable "HOME"

[<DllImport("kernel32.dll")>]
extern uint32 GetCurrentProcessId()

let pid () = GetCurrentProcessId()

let plugin (typeName: string) = Activator.CreateInstance(Type.GetType typeName)
