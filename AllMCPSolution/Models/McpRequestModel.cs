namespace AllMCPSolution.Models;

public class McpRequest
{
    public string Method { get; set; } = string.Empty;
    public Dictionary<string, object>? Params { get; set; }
    public string? Id { get; set; }
}
