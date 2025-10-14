
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
            // Get the schema from the tool's GetOpenApiSchema method
            var toolSchema = tool.GetOpenApiSchema() as dynamic;
            
            paths[$"/tools/{tool.Name}"] = new
            {
                post = new
                {
                    operationId = tool.Name,
                    summary = tool.Description,
                    description = tool.Description,
                    requestBody = new
                    {
                        required = false,
                        content = new
                        {
                            application__json = new
                            {
                                schema = new
                                {
                                    type = "object",
                                    properties = GetToolParameterProperties(tool),
                                    additionalProperties = true
                                }
                            }
                        }
                    },
                    responses = new
                    {
                        _200 = new
                        {
                            description = "Successful response",
                            content = new
                            {
                                application__json = new
                                {
                                    schema = new
                                    {
                                        type = "object"
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        return new
        {
            openapi = "3.1.0",
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

    private Dictionary<string, object> GetToolParameterProperties(IToolBase tool)
    {
        // Get the tool definition which contains the inputSchema
        var definition = tool.GetToolDefinition() as dynamic;
        if (definition?.inputSchema?.properties != null)
        {
            return definition.inputSchema.properties as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        return new Dictionary<string, object>();
    }
    
    public object GenerateAnthropicManifest(IServiceProvider? scopedProvider = null)
    {
        var tools = _toolRegistry.GetAllTools(scopedProvider)
            .Select(tool => tool.GetToolDefinition())
            .ToList();

        return new
        {
            model = "claude-3-5-sonnet-20241022",
            max_tokens = 4096,
            tools = tools,
            metadata = new
            {
                name = "AllMCPSolution",
                version = "1.0.0",
                description = "MCP server with auto-discovered tools for Anthropic Claude",
                endpoint = $"{_baseUrl}/mcp"
            }
        };
    }
}
