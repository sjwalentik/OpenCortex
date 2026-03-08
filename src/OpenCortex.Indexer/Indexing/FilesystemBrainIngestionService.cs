using System.Security.Cryptography;
using System.Text;
using OpenCortex.Core.Brains;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Indexer.Indexing;

public sealed class FilesystemBrainIngestionService
{
    private readonly FileSystemMarkdownDiscovery _discovery = new();
    private readonly MarkdownDocumentParser _parser = new();
    private readonly IEmbeddingProvider _embeddingProvider;

    public FilesystemBrainIngestionService(IEmbeddingProvider embeddingProvider)
    {
        _embeddingProvider = embeddingProvider;
    }

    public async Task<BrainIngestionBatch> IngestAsync(BrainDefinition brain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);

        if (brain.Mode != BrainMode.Filesystem)
        {
            return new BrainIngestionBatch(brain.BrainId, [], [], [], [], []);
        }

        var discoveredFiles = _discovery.DiscoverFiles(brain);
        var documents = new List<DocumentRecord>();
        var chunks = new List<ChunkRecord>();
        var edges = new List<LinkEdgeRecord>();
        var embeddings = new List<EmbeddingRecord>();
        var linkCandidates = new List<LinkCandidate>();
        var documentAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indexedAt = DateTimeOffset.UtcNow;

        foreach (var file in discoveredFiles)
        {
            var markdown = File.ReadAllText(file.AbsolutePath);
            var parsed = _parser.Parse(markdown);
            var title = ResolveTitle(file.CanonicalPath, parsed);
            var documentId = BuildStableId(brain.BrainId, file.CanonicalPath);

            documents.Add(new DocumentRecord(
                documentId,
                brain.BrainId,
                file.SourceRootId,
                file.CanonicalPath,
                title,
                parsed.Frontmatter.TryGetValue("type", out var documentType) ? documentType : null,
                parsed.Frontmatter,
                ComputeSha256(markdown),
                file.LastModifiedAt,
                indexedAt,
                false));

            RegisterDocumentAliases(documentAliases, documentId, file.CanonicalPath, title);

            foreach (var chunk in parsed.Chunks)
            {
                var chunkId = BuildStableId(documentId, chunk.ChunkIndex.ToString());
                var embedding = await _embeddingProvider.GenerateAsync(chunk.Content, cancellationToken);
                chunks.Add(new ChunkRecord(
                    chunkId,
                    brain.BrainId,
                    documentId,
                    chunk.ChunkIndex,
                    chunk.Content,
                    chunk.HeadingPath,
                    chunk.TokenCount));

                embeddings.Add(new EmbeddingRecord(
                    BuildStableId(chunkId, embedding.Model),
                    brain.BrainId,
                    chunkId,
                    embedding.Model,
                    embedding.Dimensions,
                    embedding.Vector));
            }

            foreach (var wikiLink in parsed.WikiLinks)
            {
                linkCandidates.Add(new LinkCandidate(documentId, wikiLink));
            }
        }

        foreach (var linkCandidate in linkCandidates)
        {
            documentAliases.TryGetValue(NormalizeReference(linkCandidate.TargetRef), out var toDocumentId);

            edges.Add(new LinkEdgeRecord(
                BuildStableId(linkCandidate.FromDocumentId, linkCandidate.TargetRef),
                brain.BrainId,
                linkCandidate.FromDocumentId,
                toDocumentId,
                linkCandidate.TargetRef,
                null,
                "wiki"));
        }

        return new BrainIngestionBatch(
            brain.BrainId,
            brain.SourceRoots.Select(sourceRoot => sourceRoot.SourceRootId).ToArray(),
            documents,
            chunks,
            edges,
            embeddings);
    }

    private static void RegisterDocumentAliases(Dictionary<string, string> aliases, string documentId, string canonicalPath, string title)
    {
        var pathWithoutExtension = Path.ChangeExtension(canonicalPath, null)?.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(canonicalPath);

        RegisterAlias(aliases, canonicalPath, documentId);
        RegisterAlias(aliases, pathWithoutExtension, documentId);
        RegisterAlias(aliases, fileName, documentId);
        RegisterAlias(aliases, title, documentId);
    }

    private static void RegisterAlias(Dictionary<string, string> aliases, string? alias, string documentId)
    {
        var normalized = NormalizeReference(alias);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        aliases.TryAdd(normalized, documentId);
    }

    private static string NormalizeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace('\\', '/')
            .Trim()
            .TrimStart('.', '/')
            .TrimEnd('/')
            .ToLowerInvariant();
    }

    private static string ResolveTitle(string canonicalPath, ParsedMarkdownDocument parsed)
    {
        if (parsed.Frontmatter.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var firstHeading = parsed.Chunks
            .Select(chunk => chunk.HeadingPath)
            .FirstOrDefault(heading => !string.IsNullOrWhiteSpace(heading));

        if (!string.IsNullOrWhiteSpace(firstHeading))
        {
            return firstHeading;
        }

        return Path.GetFileNameWithoutExtension(canonicalPath);
    }

    private static string BuildStableId(string left, string right)
    {
        return ComputeSha256($"{left}::{right}");
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record LinkCandidate(string FromDocumentId, string TargetRef);
}
