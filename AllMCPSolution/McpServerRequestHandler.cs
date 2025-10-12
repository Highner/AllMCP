
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public class McpServer
{
    private readonly ToolRegistry _toolRegistry;

    public McpServer(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public async Task<McpResponse> HandleRequestAsync(Dictionary<string, object>? request)
    {
        if (request == null)
        {
            return new McpResponse
            {
                Error = new McpError
                {
                    Code = -32600,
                    Message = "Invalid Request"
                }
            };
        }

        var method = request.ContainsKey("method") ? request["method"]?.ToString() : null;
        var id = request.ContainsKey("id") ? request["id"]?.ToString() : null;

        try
        {
            return method switch
            {
                "tools/list" => await HandleToolsListAsync(id),
                "tools/call" => await HandleToolCallAsync(request, id),
                "initialize" => HandleInitialize(id),
                _ => new McpResponse
                {
                    Id = id,
                    Error = new McpError
                    {
                        Code = -32601,
                        Message = $"Method not found: {method}"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new McpResponse
            {
                Id = id,
                Error = new McpError
                {
                    Code = -32603,
                    Message = "Internal error",
                    Data = ex.Message
                }
            };
        }
    }

    private McpResponse HandleInitialize(string? id)
    {
        return new McpResponse
        {
            Id = id,
            Result = new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new
                {
                    name = "AllMCPSolution",
                    version = "1.0.0"
                },
                capabilities = new
                {
                    tools = new { }
                }
            }
        };
    }

    private Task<McpResponse> HandleToolsListAsync(string? id)
    {
        var tools = _toolRegistry.GetAllTools()
            .Select(t => t.GetToolDefinition())
            .ToArray();

        return Task.FromResult(new McpResponse
        {
            Id = id,
            Result = new
            {
                tools = tools
            }
        });
    }

    private async Task<McpResponse> HandleToolCallAsync(Dictionary<string, object> request, string? id)
    {
        if (!request.ContainsKey("params"))
        {
            return new McpResponse
            {
                Id = id,
                Error = new McpError
                {
                    Code = -32602,
                    Message = "Invalid params"
                }
            };
        }

        var paramsDict = request["params"] as Dictionary<string, object>;
        var toolName = paramsDict?.ContainsKey("name") == true ? paramsDict["name"]?.ToString() : null;

        if (string.IsNullOrEmpty(toolName))
        {
            return new McpResponse
            {
                Id = id,
                Error = new McpError
                {
                    Code = -32602,
                    Message = "Tool name is required"
                }
            };
        }

        var tool = _toolRegistry.GetTool(toolName);
        if (tool == null)
        {
            return new McpResponse
            {
                Id = id,
                Error = new McpError
                {
                    Code = -32602,
                    Message = $"Unknown tool: {toolName}"
                }
            };
        }

        var arguments = paramsDict?.ContainsKey("arguments") == true 
            ? paramsDict["arguments"] as Dictionary<string, object> 
            : null;

        var result = await tool.ExecuteAsync(arguments);
        
        return new McpResponse
        {
            Id = id,
            Result = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = result.ToString()
                    }
                }
            }
        };
    }
}
