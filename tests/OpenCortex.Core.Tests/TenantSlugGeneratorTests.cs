using OpenCortex.Core.Tenancy;

namespace OpenCortex.Core.Tests;

public sealed class TenantSlugGeneratorTests
{
    // -------------------------------------------------------------------------
    // CreateSlugSeed
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateSlugSeed_WithDisplayName_UsesDisplayName()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("Alice Smith", "alice@example.com", "fallback");

        Assert.Equal("alice-smith", slug);
    }

    [Fact]
    public void CreateSlugSeed_WithNullDisplayName_FallsBackToEmailPrefix()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed(null, "bob.jones@example.com", "fallback");

        Assert.Equal("bob-jones", slug);
    }

    [Fact]
    public void CreateSlugSeed_WithEmptyDisplayName_FallsBackToEmailPrefix()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("   ", "charlie@example.com", "fallback");

        Assert.Equal("charlie", slug);
    }

    [Fact]
    public void CreateSlugSeed_WithSpecialCharacters_RemovesThem()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("Test! User @123", "user@example.com", "fallback");

        Assert.Equal("test-user-123", slug);
    }

    [Fact]
    public void CreateSlugSeed_WithUnicodeCharacters_RemovesThem()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("Tést Üser", "user@example.com", "fallback");

        Assert.Equal("t-st-ser", slug);
    }

    [Fact]
    public void CreateSlugSeed_TruncatesLongNames()
    {
        var longName = new string('a', 50);

        var slug = TenantSlugGenerator.CreateSlugSeed(longName, "user@example.com", "fallback");

        Assert.Equal(24, slug.Length);
        Assert.Equal(new string('a', 24), slug);
    }

    [Fact]
    public void CreateSlugSeed_TruncatesAndTrimsTrailingHyphen()
    {
        // Create a name that will result in a hyphen at position 24
        var slug = TenantSlugGenerator.CreateSlugSeed("abcdefghijklmnopqrstuvw-xyz", "user@example.com", "fallback");

        Assert.True(slug.Length <= 24);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void CreateSlugSeed_WithOnlySpecialCharacters_FallsBackToStableId()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("!@#$%", "!!!@example.com", "user_12345678");

        Assert.Equal("user-12345678", slug);
    }

    [Fact]
    public void CreateSlugSeed_WithEmptyEverything_ReturnsUser()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("", "@", "");

        Assert.Equal("user", slug);
    }

    [Fact]
    public void CreateSlugSeed_LowercasesInput()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("ALICE SMITH", "alice@example.com", "fallback");

        Assert.Equal("alice-smith", slug);
    }

    [Fact]
    public void CreateSlugSeed_TrimsLeadingAndTrailingHyphens()
    {
        var slug = TenantSlugGenerator.CreateSlugSeed("---test---", "user@example.com", "fallback");

        Assert.Equal("test", slug);
    }

    // -------------------------------------------------------------------------
    // GetStableSuffix
    // -------------------------------------------------------------------------

    [Fact]
    public void GetStableSuffix_ShortValue_ReturnsWholeValue()
    {
        var suffix = TenantSlugGenerator.GetStableSuffix("abc");

        Assert.Equal("abc", suffix);
    }

    [Fact]
    public void GetStableSuffix_ExactlyEightChars_ReturnsWholeValue()
    {
        var suffix = TenantSlugGenerator.GetStableSuffix("12345678");

        Assert.Equal("12345678", suffix);
    }

    [Fact]
    public void GetStableSuffix_LongerValue_ReturnsLastEightChars()
    {
        var suffix = TenantSlugGenerator.GetStableSuffix("user_abc123def456");

        Assert.Equal("3def456", suffix.Substring(1)); // last 7 chars
        Assert.Equal(8, suffix.Length);
    }

    // -------------------------------------------------------------------------
    // BuildCustomerSlug / BuildBrainSlug
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildCustomerSlug_CombinesSlugSeedAndSuffix()
    {
        var slug = TenantSlugGenerator.BuildCustomerSlug("alice", "user_12345678");

        Assert.Equal("personal-alice-12345678", slug);
    }

    [Fact]
    public void BuildBrainSlug_CombinesSlugSeedAndSuffix()
    {
        var slug = TenantSlugGenerator.BuildBrainSlug("alice", "cust_12345678");

        Assert.Equal("personal-alice-12345678", slug);
    }

    // -------------------------------------------------------------------------
    // BuildWorkspaceName
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceName_WithDisplayName_UsesDisplayName()
    {
        var name = TenantSlugGenerator.BuildWorkspaceName("Alice Smith", "alice@example.com");

        Assert.Equal("Alice Smith's Workspace", name);
    }

    [Fact]
    public void BuildWorkspaceName_WithNullDisplayName_UsesEmail()
    {
        var name = TenantSlugGenerator.BuildWorkspaceName(null, "alice@example.com");

        Assert.Equal("alice@example.com's Workspace", name);
    }

    [Fact]
    public void BuildWorkspaceName_WithEmptyDisplayName_UsesEmail()
    {
        var name = TenantSlugGenerator.BuildWorkspaceName("   ", "bob@example.com");

        Assert.Equal("bob@example.com's Workspace", name);
    }

    [Fact]
    public void BuildWorkspaceName_TrimsDisplayName()
    {
        var name = TenantSlugGenerator.BuildWorkspaceName("  Alice Smith  ", "alice@example.com");

        Assert.Equal("Alice Smith's Workspace", name);
    }

    // -------------------------------------------------------------------------
    // BuildBrainName
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildBrainName_WithDisplayName_UsesDisplayName()
    {
        var name = TenantSlugGenerator.BuildBrainName("Alice Smith", "alice@example.com");

        Assert.Equal("Alice Smith's Brain", name);
    }

    [Fact]
    public void BuildBrainName_WithNullDisplayName_UsesEmail()
    {
        var name = TenantSlugGenerator.BuildBrainName(null, "alice@example.com");

        Assert.Equal("alice@example.com's Brain", name);
    }

    [Fact]
    public void BuildBrainName_WithEmptyDisplayName_UsesEmail()
    {
        var name = TenantSlugGenerator.BuildBrainName("   ", "bob@example.com");

        Assert.Equal("bob@example.com's Brain", name);
    }

    [Fact]
    public void BuildBrainName_TrimsDisplayName()
    {
        var name = TenantSlugGenerator.BuildBrainName("  Alice Smith  ", "alice@example.com");

        Assert.Equal("Alice Smith's Brain", name);
    }
}
