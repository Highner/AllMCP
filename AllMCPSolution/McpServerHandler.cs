using AllMCPSolution.Models;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Services;

public class McpServer
{
    private readonly HelloWorldTool _helloWorldTool;

    public McpServer(HelloWorldTool helloWorldTool)
    {
        _helloWorldTool = helloWorldTool;
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
        var tools = new[]
        {
            _helloWorldTool.GetToolDefinition()
        };

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

        if (toolName == _helloWorldTool.Name)
        {
            var result = await _helloWorldTool.ExecuteAsync();
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
                            text = result
                        }
                    }
                }
            };
        }

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
}
