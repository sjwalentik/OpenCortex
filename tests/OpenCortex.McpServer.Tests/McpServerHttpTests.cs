using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenCortex.McpServer.Tests;

public sealed class McpServerHttpTests : IClassFixture<McpServerHttpTests.McpServerWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpServerHttpTests(McpServerWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_ReturnsToolManifestMetadata()
    {
        var response = await _client.GetAsync("/");

        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OpenCortex.McpServer", json.RootElement.GetProperty("service").GetString());
        Assert.Equal("/tool-manifest", json.RootElement.GetProperty("toolManifestUrl").GetString());
        Assert.True(json.RootElement.GetProperty("toolCount").GetInt32() >= 9);
    }

    [Fact]
    public async Task ToolManifest_ReturnsPublishedToolList_WithoutAuthentication()
    {
        var response = await _client.GetAsync("/tool-manifest");

        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tools = json.RootElement.GetProperty("tools").EnumerateArray().ToList();
        var toolNames = tools.Select(tool => tool.GetProperty("name").GetString()).ToList();

        Assert.True(json.RootElement.GetProperty("count").GetInt32() >= 9);
        Assert.Contains(tools, tool => string.Equals(tool.GetProperty("name").GetString(), "get_document", StringComparison.Ordinal));
        Assert.Contains(tools, tool => string.Equals(tool.GetProperty("name").GetString(), "save_document", StringComparison.Ordinal));
        Assert.True(toolNames.IndexOf("save_document") < toolNames.IndexOf("create_document"));
        Assert.True(toolNames.IndexOf("save_document") < toolNames.IndexOf("update_document"));

        var getDocument = tools.Single(tool => string.Equals(tool.GetProperty("name").GetString(), "get_document", StringComparison.Ordinal));
        var parameters = getDocument.GetProperty("parameters").EnumerateArray().ToList();

        Assert.Contains(parameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "brain_id", StringComparison.Ordinal));
        Assert.Contains(parameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "document_id", StringComparison.Ordinal));
        Assert.Contains(parameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "canonical_path", StringComparison.Ordinal));

        var saveDocument = tools.Single(tool => string.Equals(tool.GetProperty("name").GetString(), "save_document", StringComparison.Ordinal));
        var saveParameters = saveDocument.GetProperty("parameters").EnumerateArray().ToList();

        Assert.Contains(saveParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "brain_id", StringComparison.Ordinal));
        Assert.Contains(saveParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "canonical_path", StringComparison.Ordinal));
        Assert.Contains(saveParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "content", StringComparison.Ordinal));
        Assert.Contains("Preferred write tool", saveDocument.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        var deleteDocument = tools.Single(tool => string.Equals(tool.GetProperty("name").GetString(), "delete_document", StringComparison.Ordinal));
        var deleteParameters = deleteDocument.GetProperty("parameters").EnumerateArray().ToList();

        Assert.Contains(deleteParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "brain_id", StringComparison.Ordinal));
        Assert.Contains(deleteParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "managed_document_id", StringComparison.Ordinal));
        Assert.Contains(deleteParameters, parameter => string.Equals(parameter.GetProperty("name").GetString(), "canonical_path", StringComparison.Ordinal));
    }

    public sealed class McpServerWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            Environment.SetEnvironmentVariable("OpenCortex__Database__ConnectionString", "Host=localhost;Database=opencortex_test");
        }
    }
}
