using System.Globalization;

namespace OpenCortex.Core.Embeddings;

public static class EmbeddingVector
{
    public static string ToVectorLiteral(IReadOnlyList<float> vector)
    {
        return "[" + string.Join(',', vector.Select(value => value.ToString("G9", CultureInfo.InvariantCulture))) + "]";
    }
}
