
using AllMCPSolution.Tools;
using System.Text.Json;

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
                    requestBody = new Dictionary<string, object>
                    {
                        ["required"] = false,
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = GetToolRequestBodySchema(tool)
                            }
                        }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new Dictionary<string, object>
                        {
                            ["description"] = "Successful response",
                            ["content"] = new Dictionary<string, object>
                            {
                                ["application/json"] = new Dictionary<string, object>
                                {
                                    ["schema"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new Dictionary<string, object>()
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

    private JsonElement GetToolRequestBodySchema(IToolBase tool)
    {
        // Get the tool definition which has the inputSchema with proper structure
        var definition = tool.GetToolDefinition();
        
        // Serialize to JSON and deserialize back as JsonElement for proper nested object handling
        var json = JsonSerializer.Serialize(definition);
        using var doc = JsonDocument.Parse(json);
        
        if (doc.RootElement.TryGetProperty("inputSchema", out var inputSchemaElement))
        {
            return inputSchemaElement.Clone();
        }
        
        // Return empty schema as JsonElement
        var emptySchema = JsonSerializer.Serialize(new { type = "object", properties = new { } });
        using var emptyDoc = JsonDocument.Parse(emptySchema);
        return emptyDoc.RootElement.Clone();
    }
    
    public object GenerateAnthropicManifest(IServiceProvider? scopedProvider = null)
    {
        var toolsRaw = _toolRegistry.GetAllTools(scopedProvider)
            .Select(tool => tool.GetToolDefinition())
            .ToList();

        // Normalize tool definitions for Anthropic: it expects `input_schema` (snake_case)
        var tools = new List<object>(toolsRaw.Count);
        foreach (var def in toolsRaw)
        {
            // Use JSON-based safe extraction to avoid RuntimeBinderException on missing members
            var json = JsonSerializer.Serialize(def);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            string? description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
            string? safety = root.TryGetProperty("safety", out var safetyEl) && safetyEl.ValueKind == JsonValueKind.String ? safetyEl.GetString() : null;

            JsonElement? inputSchema = null;
            if (root.TryGetProperty("inputSchema", out var inputSchemaEl))
                inputSchema = inputSchemaEl.Clone();
            else if (root.TryGetProperty("input_schema", out var inputSchemaSnakeEl))
                inputSchema = inputSchemaSnakeEl.Clone();

            if (inputSchema.HasValue)
            {
                tools.Add(new
                {
                    name = name,
                    description = description,
                    safety = safety,
                    input_schema = inputSchema.Value
                });
            }
            else
            {
                // Fallback: include basic fields without schema
                tools.Add(new { name = name, description = description, safety = safety });
            }
        }

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

    public object GenerateOpenAIMcpDiscovery(IServiceProvider? scopedProvider = null)
    {
        var toolsRaw = _toolRegistry.GetAllTools(scopedProvider)
            .Select(tool => tool.GetToolDefinition())
            .ToList();

        // For OpenAI Agent Builder MCP discovery, include endpoint and tools with both inputSchema and input_schema keys
        var tools = new List<object>(toolsRaw.Count);
        foreach (var def in toolsRaw)
        {
            // Use JSON-based safe extraction to avoid RuntimeBinderException on missing members
            var json = JsonSerializer.Serialize(def);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            string? description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
            string? safety = root.TryGetProperty("safety", out var safetyEl) && safetyEl.ValueKind == JsonValueKind.String ? safetyEl.GetString() : null;

            JsonElement? inputSchema = null;
            if (root.TryGetProperty("inputSchema", out var inputSchemaEl))
                inputSchema = inputSchemaEl.Clone();
            else if (root.TryGetProperty("input_schema", out var inputSchemaSnakeEl))
                inputSchema = inputSchemaSnakeEl.Clone();

            if (inputSchema.HasValue)
            {
                tools.Add(new
                {
                    name = name,
                    description = description,
                    safety = safety,
                    inputSchema = inputSchema.Value,
                    input_schema = inputSchema.Value
                });
            }
            else
            {
                tools.Add(new { name = name, description = description, safety = safety });
            }
        }

        return new
        {
            schemaVersion = "1.0",
            mcp = new
            {
                endpoint = new { url = $"{_baseUrl}/mcp", transport = "http", methods = new[] { "POST" } },
                capabilities = new { tools = true }
            },
            tools = tools
        };
    }
}
