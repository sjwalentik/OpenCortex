using System.Security.Claims;
using OpenCortex.Api;

namespace OpenCortex.Api.Tests;

public sealed class HostedTenantClaimsTests
{
    [Fact]
    public void TryCreateProfile_WithStandardClaims_ReturnsProfile()
    {
        var principal = CreatePrincipal(
            (ClaimTypes.NameIdentifier, "firebase-uid-123"),
            (ClaimTypes.Email, "alice@example.com"),
            ("name", "Alice Smith"),
            ("picture", "https://example.com/alice.jpg"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal("firebase-uid-123", profile.ExternalId);
        Assert.Equal("alice@example.com", profile.Email);
        Assert.Equal("Alice Smith", profile.DisplayName);
        Assert.Equal("https://example.com/alice.jpg", profile.AvatarUrl);
    }

    [Fact]
    public void TryCreateProfile_WithFirebaseClaims_ReturnsProfile()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid-456"),
            ("email", "bob@example.com"),
            ("name", "Bob Jones"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal("firebase-uid-456", profile.ExternalId);
        Assert.Equal("bob@example.com", profile.Email);
        Assert.Equal("Bob Jones", profile.DisplayName);
        Assert.Null(profile.AvatarUrl);
    }

    [Fact]
    public void TryCreateProfile_WithUserIdClaim_ReturnsProfile()
    {
        var principal = CreatePrincipal(
            ("user_id", "custom-uid-789"),
            ("email", "charlie@example.com"),
            ("name", "Charlie Brown"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.NotNull(profile);
        Assert.Equal("custom-uid-789", profile.ExternalId);
    }

    [Fact]
    public void TryCreateProfile_MissingSubject_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            ("email", "nosubject@example.com"),
            ("name", "No Subject"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.False(result);
        Assert.Null(profile);
        Assert.NotNull(error);
        Assert.Contains("subject", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateProfile_MissingEmail_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid-no-email"),
            ("name", "No Email User"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.False(result);
        Assert.Null(profile);
        Assert.NotNull(error);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateProfile_MissingDisplayName_FallsBackToEmailPrefix()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid-no-name"),
            ("email", "noname@example.com"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.NotNull(profile);
        Assert.Equal("noname", profile.DisplayName);
    }

    [Fact]
    public void TryCreateProfile_EmptySubject_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            ("sub", "   "),
            ("email", "user@example.com"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.False(result);
        Assert.Null(profile);
        Assert.NotNull(error);
        Assert.Contains("subject", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateProfile_EmptyEmail_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid"),
            ("email", "   "));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.False(result);
        Assert.Null(profile);
        Assert.NotNull(error);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateProfile_PrefersNameIdentifierOverSub()
    {
        var principal = CreatePrincipal(
            (ClaimTypes.NameIdentifier, "preferred-uid"),
            ("sub", "fallback-uid"),
            ("email", "user@example.com"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.NotNull(profile);
        Assert.Equal("preferred-uid", profile.ExternalId);
    }

    [Fact]
    public void TryCreateProfile_PrefersClaimTypeEmailOverRawEmail()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid"),
            (ClaimTypes.Email, "preferred@example.com"),
            ("email", "fallback@example.com"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.NotNull(profile);
        Assert.Equal("preferred@example.com", profile.Email);
    }

    [Fact]
    public void TryCreateProfile_PrefersNameClaimOverClaimTypeName()
    {
        var principal = CreatePrincipal(
            ("sub", "firebase-uid"),
            ("email", "user@example.com"),
            ("name", "Preferred Name"),
            (ClaimTypes.Name, "Fallback Name"));

        var result = HostedTenantClaims.TryCreateProfile(principal, out var profile, out var error);

        Assert.True(result);
        Assert.NotNull(profile);
        Assert.Equal("Preferred Name", profile.DisplayName);
    }

    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "Test");

        return new ClaimsPrincipal(identity);
    }
}
