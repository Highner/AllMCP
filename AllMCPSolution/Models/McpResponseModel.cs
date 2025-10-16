namespace AllMCPSolution.Models;

public class McpResponse
{
    // JSON-RPC 2.0 requires the "jsonrpc" field; many clients will hang without it
    public string Jsonrpc { get; set; } = "2.0";
    public string? Id { get; set; }
    public object? Result { get; set; }
    public McpError? Error { get; set; }
}

public class McpError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
