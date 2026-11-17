using System.Runtime.InteropServices;

namespace CSharp.Effects;

/// Categories hidden by default, native calls and blind spots.
public static partial class Hidden
{
    public static DateTime Now() => DateTime.Now;

    public static string? Home() => Environment.GetEnvironmentVariable("HOME");

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    public static uint Pid() => GetCurrentProcessId();

    public static object? Plugin(string typeName) => Activator.CreateInstance(Type.GetType(typeName)!);
}
