
using AllMCPSolution.Tools;

namespace AllMCPSolution.Services;

public class ManifestGenerator
{
    private readonly ToolRegistry _toolRegistry;
    private readonly string _baseUrl;

    public ManifestGenerator(ToolRegistry toolRegistry, IConfiguration configuration)
    {
        _toolRegistry = toolRegistry;
        _baseUrl = configuration["BaseUrl"] ?? "https://allmcp-azfthzccbub7a3e5.northeurope-01.azurewebsites.net";
    }

    public object GenerateMcpManifest()
    {
        return new
        {
            schemaVersion = "1.0",
            name = "AllMCPSolution",
            version = "1.0.0",
            description = "MCP server with auto-discovered tools",
            protocol = "mcp",
            protocolVersion = "2024-11-05",
            endpoints = new
            {
                mcp = new
                {
                    url = $"{_baseUrl}/mcp",
                    transport = "http",
                    methods = new[] { "POST" }
                }
            },
            capabilities = new
            {
                tools = true,
                prompts = false,
                resources = false
            },
            metadata = new
            {
                homepage = _baseUrl,
                documentation = $"{_baseUrl}/docs"
            }
        };
    }

    public object GenerateOpenApiManifest(IServiceProvider? scopedProvider = null)
    {
        var tools = _toolRegistry.GetAllTools(scopedProvider).ToList();
        var paths = new Dictionary<string, object>();

        foreach (var tool in tools)
        {
            paths[$"/tools/{tool.Name}"] = new
            {
                post = tool.GetOpenApiSchema()
            };
        }

        return new
        {
            openapi = "3.0.0",
            info = new
            {
                title = "AllMCPSolution API",
                version = "1.0.0",
                description = "Auto-generated API for MCP tools"
            },
            servers = new[]
            {
                new { url = _baseUrl }
            },
            paths = paths
        };
    }
}
