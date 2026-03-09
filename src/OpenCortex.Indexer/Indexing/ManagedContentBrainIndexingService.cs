using System.Security.Cryptography;
using System.Text;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Indexer.Indexing;

public sealed class ManagedContentBrainIndexingService : IManagedContentBrainIndexingService
{
    private readonly IManagedDocumentStore _managedDocumentStore;
    private readonly IDocumentCatalogStore _documentStore;
    private readonly IChunkStore _chunkStore;
    private readonly ILinkGraphStore _linkGraphStore;
    private readonly IIndexRunStore _indexRunStore;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly MarkdownDocumentParser _parser = new();

    public ManagedContentBrainIndexingService(
        IManagedDocumentStore managedDocumentStore,
        IDocumentCatalogStore documentStore,
        IChunkStore chunkStore,
        ILinkGraphStore linkGraphStore,
        IIndexRunStore indexRunStore,
        IEmbeddingStore embeddingStore,
        IEmbeddingProvider embeddingProvider)
    {
        _managedDocumentStore = managedDocumentStore;
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _linkGraphStore = linkGraphStore;
        _indexRunStore = indexRunStore;
        _embeddingStore = embeddingStore;
        _embeddingProvider = embeddingProvider;
    }

    public async Task<IndexRunRecord> ReindexAsync(
        string customerId,
        string brainId,
        string triggerType,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var managedDocuments = await _managedDocumentStore.ListManagedDocumentsForIndexingAsync(customerId, brainId, cancellationToken);
        var batch = await BuildBatchAsync(brainId, managedDocuments, startedAt, cancellationToken);

        var indexRun = new IndexRunRecord(
            Guid.NewGuid().ToString("n"),
            brainId,
            triggerType,
            "running",
            startedAt,
            null,
            batch.Documents.Count,
            0,
            0,
            null);

        await _indexRunStore.StartIndexRunAsync(indexRun, cancellationToken);

        try
        {
            var activeDocumentIds = batch.Documents.Select(document => document.DocumentId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeChunkIds = batch.Chunks.Select(chunk => chunk.ChunkId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeEdgeIds = batch.LinkEdges.Select(edge => edge.LinkEdgeId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var activeEmbeddingIds = batch.Embeddings.Select(embedding => embedding.EmbeddingId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            await _documentStore.UpsertDocumentsAsync(batch.Documents, cancellationToken);
            await _documentStore.MarkMissingManagedDocumentsDeletedAsync(
                brainId,
                batch.Documents.Select(document => document.CanonicalPath).ToArray(),
                startedAt,
                cancellationToken);

            await _chunkStore.UpsertChunksAsync(batch.Chunks, cancellationToken);
            await _chunkStore.DeleteStaleChunksAsync(brainId, activeChunkIds, activeDocumentIds, cancellationToken);
            await _linkGraphStore.UpsertEdgesAsync(batch.LinkEdges, cancellationToken);
            await _linkGraphStore.DeleteStaleEdgesAsync(brainId, activeEdgeIds, activeDocumentIds, cancellationToken);
            await _embeddingStore.UpsertEmbeddingsAsync(batch.Embeddings, cancellationToken);
            await _embeddingStore.DeleteStaleEmbeddingsAsync(brainId, activeEmbeddingIds, activeChunkIds, cancellationToken);

            var completedRun = indexRun with
            {
                Status = "completed",
                CompletedAt = DateTimeOffset.UtcNow,
                DocumentsIndexed = batch.Documents.Count,
            };

            await _indexRunStore.CompleteIndexRunAsync(completedRun, cancellationToken);
            return completedRun;
        }
        catch (Exception ex)
        {
            await _indexRunStore.AddIndexRunErrorAsync(
                new IndexRunErrorRecord(
                    Guid.NewGuid().ToString("n"),
                    indexRun.IndexRunId,
                    null,
                    null,
                    ex.GetType().Name,
                    ex.Message,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            var failedRun = indexRun with
            {
                Status = "failed",
                CompletedAt = DateTimeOffset.UtcNow,
                DocumentsFailed = batch.Documents.Count,
                ErrorSummary = ex.Message,
            };

            await _indexRunStore.CompleteIndexRunAsync(failedRun, cancellationToken);
            throw;
        }
    }

    private async Task<BrainIngestionBatch> BuildBatchAsync(
        string brainId,
        IReadOnlyList<ManagedDocumentDetail> managedDocuments,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken)
    {
        var documents = new List<DocumentRecord>();
        var chunks = new List<ChunkRecord>();
        var edges = new List<LinkEdgeRecord>();
        var embeddings = new List<EmbeddingRecord>();
        var linkCandidates = new List<LinkCandidate>();
        var documentAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var managedDocument in managedDocuments)
        {
            var parsed = _parser.Parse(managedDocument.Content);
            var documentId = managedDocument.ManagedDocumentId;

            documents.Add(new DocumentRecord(
                documentId,
                brainId,
                null,
                managedDocument.CanonicalPath,
                managedDocument.Title,
                managedDocument.Frontmatter.TryGetValue("type", out var documentType) ? documentType : null,
                managedDocument.Frontmatter,
                managedDocument.ContentHash,
                managedDocument.UpdatedAt,
                indexedAt,
                false));

            RegisterDocumentAliases(documentAliases, managedDocument, documentId);

            foreach (var chunk in parsed.Chunks)
            {
                var chunkId = BuildStableId(documentId, chunk.ChunkIndex.ToString());
                var embedding = await _embeddingProvider.GenerateAsync(chunk.Content, cancellationToken);
                chunks.Add(new ChunkRecord(
                    chunkId,
                    brainId,
                    documentId,
                    chunk.ChunkIndex,
                    chunk.Content,
                    chunk.HeadingPath,
                    chunk.TokenCount));

                embeddings.Add(new EmbeddingRecord(
                    BuildStableId(chunkId, embedding.Model),
                    brainId,
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
                brainId,
                linkCandidate.FromDocumentId,
                toDocumentId,
                linkCandidate.TargetRef,
                null,
                "wiki"));
        }

        return new BrainIngestionBatch(
            brainId,
            [],
            documents,
            chunks,
            edges,
            embeddings);
    }

    private static void RegisterDocumentAliases(
        Dictionary<string, string> aliases,
        ManagedDocumentDetail managedDocument,
        string documentId)
    {
        var pathWithoutExtension = Path.ChangeExtension(managedDocument.CanonicalPath, null)?.Replace('\\', '/');

        RegisterAlias(aliases, managedDocument.CanonicalPath, documentId);
        RegisterAlias(aliases, pathWithoutExtension, documentId);
        RegisterAlias(aliases, managedDocument.Slug, documentId);
        RegisterAlias(aliases, managedDocument.Title, documentId);
    }

    private static void RegisterAlias(Dictionary<string, string> aliases, string? alias, string documentId)
    {
        var normalized = NormalizeReference(alias);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.TryAdd(normalized, documentId);
        }
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

    private static string BuildStableId(string left, string right)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{left}::{right}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record LinkCandidate(string FromDocumentId, string TargetRef);
}
