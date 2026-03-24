using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Ollama;

namespace OpenCortex.Api.Tests;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task ListModelsAsync_FallsBackToOpenAICompatibleModelsEndpoint()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/tags" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/v1/models" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"data":[{"id":"qwen3.5-35b-a3b-instruct","owned_by":"ollama"}]}
                        """,
                        Encoding.UTF8,
                        "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var httpClient = new HttpClient(handler);
        var provider = new OllamaProvider(
            httpClient,
            Options.Create(OllamaOptions.CreateRemote("http://ollama.internal:11434")),
            NullLogger<OllamaProvider>.Instance);

        var models = await provider.ListModelsAsync();

        var model = Assert.Single(models);
        Assert.Equal("qwen3.5-35b-a3b-instruct", model.Id);
        Assert.Contains("/api/tags", handler.RequestedPaths);
        Assert.Contains("/v1/models", handler.RequestedPaths);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return Task.FromResult(_responder(request));
        }
    }
}
