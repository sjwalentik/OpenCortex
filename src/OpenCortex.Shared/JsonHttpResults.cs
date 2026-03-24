using System.Text.Json;

namespace OpenCortex.Http;

internal static class JsonHttpResults
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Text(
        object value,
        int statusCode = StatusCodes.Status200OK,
        string contentType = "application/json") =>
        Results.Text(
            JsonSerializer.Serialize(value, WebJsonOptions),
            contentType,
            statusCode: statusCode);
}
