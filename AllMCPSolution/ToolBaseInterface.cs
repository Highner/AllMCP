namespace AllMCPSolution.Tools;

public interface IToolBase
{
    string Name { get; }
    string Description { get; }
    Task<object> ExecuteAsync(Dictionary<string, object>? parameters);
    object GetToolDefinition();
    object GetOpenApiSchema();
}
