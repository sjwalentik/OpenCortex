using System.Text.RegularExpressions;

namespace OpenCortex.Persistence.Postgres;

public sealed partial class PostgresEmbeddingSchemaValidator
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresEmbeddingSchemaValidator(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(int expectedDimensions, CancellationToken cancellationToken = default)
    {
        if (expectedDimensions <= 0)
        {
            return ["OpenCortex:Embeddings:Dimensions must be greater than zero before validating the pgvector schema."];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT format_type(attribute.atttypid, attribute.atttypmod)
            FROM pg_attribute attribute
            INNER JOIN pg_class class ON class.oid = attribute.attrelid
            INNER JOIN pg_namespace namespace ON namespace.oid = class.relnamespace
            WHERE namespace.nspname = @schema
              AND class.relname = 'embeddings'
              AND attribute.attname = 'vector'
              AND attribute.attnum > 0
              AND NOT attribute.attisdropped
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("schema", _connectionFactory.Schema);

        var formattedType = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(formattedType))
        {
            return [$"Postgres schema validation failed: '{_connectionFactory.Schema}.embeddings.vector' was not found. Apply the OpenCortex migrations before starting the service."];
        }

        if (!TryParseVectorDimensions(formattedType, out var actualDimensions))
        {
            return [$"Postgres schema validation failed: '{_connectionFactory.Schema}.embeddings.vector' uses unsupported type '{formattedType}'. Expected pgvector column 'vector({expectedDimensions})'."];
        }

        if (actualDimensions != expectedDimensions)
        {
            return [$"Postgres schema validation failed: '{_connectionFactory.Schema}.embeddings.vector' is configured as vector({actualDimensions}) but OpenCortex:Embeddings:Dimensions is {expectedDimensions}. Rebuild the embeddings column/index or update the app embedding dimensions so they match."];
        }

        return [];
    }

    public static bool TryParseVectorDimensions(string? formattedType, out int dimensions)
    {
        dimensions = 0;
        if (string.IsNullOrWhiteSpace(formattedType))
        {
            return false;
        }

        var match = VectorTypeRegex().Match(formattedType.Trim());
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["dimensions"].Value, out dimensions);
    }

    [GeneratedRegex(@"^vector\((?<dimensions>\d+)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VectorTypeRegex();
}
