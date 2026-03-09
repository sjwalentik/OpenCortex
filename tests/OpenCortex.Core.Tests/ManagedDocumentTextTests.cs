using OpenCortex.Core.Authoring;

namespace OpenCortex.Core.Tests;

public sealed class ManagedDocumentTextTests
{
    [Fact]
    public void NormalizeSlug_NormalizesMixedCaseAndSymbols()
    {
        var slug = ManagedDocumentText.NormalizeSlug(" My First Doc! v1 ");

        Assert.Equal("my-first-doc-v1", slug);
    }

    [Fact]
    public void BuildCanonicalPath_AppendsMarkdownExtension()
    {
        var canonicalPath = ManagedDocumentText.BuildCanonicalPath("Quarterly Review");

        Assert.Equal("quarterly-review.md", canonicalPath);
    }

    [Fact]
    public void CountWords_CountsNonWhitespaceTokens()
    {
        var count = ManagedDocumentText.CountWords("One two\nthree\tfour");

        Assert.Equal(4, count);
    }

    [Fact]
    public void ComputeContentHash_IsStableForSameContent()
    {
        var first = ManagedDocumentText.ComputeContentHash("# Heading\nSame content");
        var second = ManagedDocumentText.ComputeContentHash("# Heading\nSame content");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }
}
