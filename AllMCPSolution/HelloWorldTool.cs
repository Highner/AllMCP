using AllMCPSolution.Attributes;

namespace AllMCPSolution.Tools;

//[McpTool("hello_world", "A simple test tool that returns 'Hello World'")]
public class HelloWorldTool : IToolBase
{
    public string Name => "hello_world";
    public string Description => "A simple test tool that returns 'Hello World'";

    public Task<object> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        return Task.FromResult<object>("Hello World");
    }

    public object GetToolDefinition()
    {
        return new
        {
            name = Name,
            description = Description,
            inputSchema = new
            {
                type = "object",
                properties = new { },
                required = new string[] { }
            }
        };
    }

    public object GetOpenApiSchema()
    {
        return new Dictionary<string, object>
        {
            ["operationId"] = Name,
            ["summary"] = Description,
            ["responses"] = new Dictionary<string, object>
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
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["result"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}