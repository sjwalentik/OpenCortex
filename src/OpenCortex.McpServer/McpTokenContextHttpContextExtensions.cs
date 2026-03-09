using Microsoft.AspNetCore.Http;

namespace OpenCortex.McpServer;

public static class McpTokenContextHttpContextExtensions
{
    private const string ItemKey = "OpenCortex.McpTokenContext";

    public static void SetMcpTokenContext(this HttpContext context, McpTokenContext tokenContext)
    {
        context.Items[ItemKey] = tokenContext;
    }

    public static McpTokenContext? GetMcpTokenContext(this HttpContext? context)
    {
        if (context?.Items.TryGetValue(ItemKey, out var value) == true)
        {
            return value as McpTokenContext;
        }

        return null;
    }
}
