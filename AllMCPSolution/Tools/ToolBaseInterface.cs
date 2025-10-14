namespace AllMCPSolution.Tools;

public interface IToolBase
{
    string Name { get; }
    string Description { get; }
    string? SafetyLevel { get; }
    Task<object> ExecuteAsync(Dictionary<string, object>? parameters);
    object GetToolDefinition();
    object GetOpenApiSchema();
}
