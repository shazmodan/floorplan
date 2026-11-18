namespace CSharp.Wrappers;

/// A wrapper over File, with two routes to one rule (filesystem-write.file).
public static class Files
{
    public static void Rotate(string path, string[] locks) => RotateCore(path, locks);

    /// The shortest route opens a lock file whose path is not known; the longer one deletes the file the caller
    /// names. The payload follows the longer route, and so must the primitive reported, also for Rotate.
    private static void RotateCore(string path, string[] locks)
    {
        File.Open(locks[0], FileMode.OpenOrCreate).Dispose();
        Remove(path);
    }

    private static void Remove(string path) => File.Delete(path);
}
