using System.Diagnostics;

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:18888");
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL") ?? "http://127.0.0.1:18889");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL") ?? "http://127.0.0.1:18889");
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS") ?? "true");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS") ?? "true");

var dashboardUrl = (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:18888")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .FirstOrDefault() ?? "http://127.0.0.1:18888";

var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    AllowUnsecuredTransport = true,
});

builder.AddProject("api", "../OpenCortex.Api/OpenCortex.Api.csproj");
builder.AddProject("mcp", "../OpenCortex.McpServer/OpenCortex.McpServer.csproj");
builder.AddProject("workers", "../OpenCortex.Workers/OpenCortex.Workers.csproj");
builder.AddProject("portal", "../OpenCortex.Portal/OpenCortex.Portal.csproj");

if (!string.Equals(Environment.GetEnvironmentVariable("OPENCORTEX_APPHOST_OPEN_BROWSER"), "false", StringComparison.OrdinalIgnoreCase))
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dashboardUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    });
}

builder.Build().Run();
