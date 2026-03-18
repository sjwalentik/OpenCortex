namespace OpenCortex.Tools;

internal static class ShellEscaping
{
    public static string SingleQuote(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }
}
