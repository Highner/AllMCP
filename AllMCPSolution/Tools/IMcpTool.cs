// File: Abstractions/IServerTool.cs
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AllMCPSolution.Tools;

public interface IMcpTool
{
    Tool GetDefinition();

    ValueTask<CallToolResult> RunAsync(CallToolRequestParams request, CancellationToken ct);
}

public interface IResourceProvider
{
    IEnumerable<Resource> ListResources();
    ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct);
}