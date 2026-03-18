namespace OpenCortex.Tools.FileSystem;

internal static class ShellEscaping
{
    public static string SingleQuote(string value)
    {
        return $"'{value.Replace("'", "'\\''")}'";
    }
}
