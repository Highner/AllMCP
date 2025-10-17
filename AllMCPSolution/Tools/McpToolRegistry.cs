// File: Infrastructure/McpToolRegistry.cs
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using AllMCPSolution.Tools;
using ModelContextProtocol.Protocol;

namespace AllMCPSolution.Tools;

public sealed class McpToolRegistry
{
    private readonly Dictionary<string, Type> _toolTypesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Assembly[] _assembliesToScan;
    private readonly IServiceProvider _rootProvider;

    public McpToolRegistry(IServiceProvider rootProvider, params Assembly[] assembliesToScan)
    {
        _rootProvider = rootProvider;
        _assembliesToScan = (assembliesToScan is { Length: > 0 })
            ? assembliesToScan
            : new[] { Assembly.GetExecutingAssembly() };
        Discover();
    }

    private void Discover()
    {
        foreach (var asm in _assembliesToScan)
        {
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IMcpTool).IsAssignableFrom(t)) continue;

                using var scope = _rootProvider.CreateScope();
                var instance = scope.ServiceProvider.GetService(t) as IMcpTool
                               ?? throw new InvalidOperationException($"Type {t.FullName} not registered for DI.");
                var def = instance.GetDefinition();
                if (string.IsNullOrWhiteSpace(def.Name))
                    throw new InvalidOperationException($"{t.FullName}: Tool name cannot be empty.");
                if (_toolTypesByName.ContainsKey(def.Name))
                    throw new InvalidOperationException($"Duplicate tool name '{def.Name}' from {t.FullName}.");

                _toolTypesByName[def.Name] = t;
            }
        }
    }

    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken ct)
    {
        using var scope = _rootProvider.CreateScope();
        var tools = _toolTypesByName.Values
            .Select(t => (scope.ServiceProvider.GetRequiredService(t) as IMcpTool)!)
            .Select(inst => inst.GetDefinition())
            .ToArray();

        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    public async ValueTask<CallToolResult> CallToolAsync(ModelContextProtocol.Protocol.CallToolRequestParams request, CancellationToken ct)
    {
        var name = request?.Name ?? string.Empty;
        if (!_toolTypesByName.TryGetValue(name, out var type))
            throw new McpException($"Unknown tool '{name}'", McpErrorCode.InvalidRequest);

        using var scope = _rootProvider.CreateScope();
        var tool = (IMcpTool)scope.ServiceProvider.GetRequiredService(type);
        var result = await tool.RunAsync(request!, ct);
        return result;
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken ct)
    {
        using var scope = _rootProvider.CreateScope();
        var providers = _toolTypesByName.Values
            .Select(t => scope.ServiceProvider.GetRequiredService(t))
            .OfType<IResourceProvider>()
            .ToArray();

        var resources = providers.SelectMany(p => p.ListResources()).ToArray();
        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    public async ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        using var scope = _rootProvider.CreateScope();
        var providers = _toolTypesByName.Values
            .Select(t => scope.ServiceProvider.GetRequiredService(t))
            .OfType<IResourceProvider>()
            .ToArray();

        foreach (var rp in providers)
        {
            var list = rp.ListResources();
            if (list.Any(r => r.Uri == request?.Uri))
            {
                return await rp.ReadResourceAsync(request!, ct);
            }
        }

        throw new McpException($"Resource not found: {request?.Uri}", McpErrorCode.InvalidParams);
    }
}
