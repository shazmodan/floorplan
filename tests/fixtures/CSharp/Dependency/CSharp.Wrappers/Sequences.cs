namespace CSharp.Wrappers;

/// A library method that enumerates the sequence it is given: a query passed to it runs there.
public static class Sequences
{
    public static int CountAll<T>(IEnumerable<T> items)
    {
        var n = 0;

        foreach (var _ in items)
            n++;

        return n;
    }
}
