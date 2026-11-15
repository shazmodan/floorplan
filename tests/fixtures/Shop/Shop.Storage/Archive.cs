namespace Shop.Storage;

/// A C# library that performs an effect, called from F#.
public static class Archive
{
    public static void Append(string path, string line) =>
        System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
}
