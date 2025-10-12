namespace AllMCPSolution.Tools;

public class HelloWorldTool
{
    public string Name => "hello_world";
    public string Description => "A simple test tool that returns 'Hello World'";

    public Task<string> ExecuteAsync()
    {
        return Task.FromResult("Hello World");
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
}
