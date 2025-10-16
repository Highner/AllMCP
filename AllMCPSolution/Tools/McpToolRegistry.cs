// File: Infrastructure/McpToolRegistry.cs
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using AllMCPSolution.Tools;
using ModelContextProtocol.Protocol;

namespace AllMCPSolution.Tools;

public sealed class McpToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _toolsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IResourceProvider> _resourceProviders = new();

    public McpToolRegistry(params Assembly[] assembliesToScan)
    {
        var assemblies = (assembliesToScan is { Length: > 0 })
            ? assembliesToScan
            : new[] { Assembly.GetExecutingAssembly() };

        foreach (var asm in assemblies)
        {
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;

                // Find types that implement IMcpTool
                if (typeof(IMcpTool).IsAssignableFrom(t))
                {
                    // Prefer static tool classes with a parameterless constructor or static GetInstance().
                    var instance = CreateInstance<IMcpTool>(t);
                    var def = instance.GetDefinition();

                    if (string.IsNullOrWhiteSpace(def.Name))
                        throw new InvalidOperationException($"{t.FullName}: Tool name cannot be empty.");

                    if (_toolsByName.ContainsKey(def.Name))
                        throw new InvalidOperationException($"Duplicate tool name '{def.Name}' from {t.FullName}.");

                    _toolsByName[def.Name] = instance;

                    // If it also provides resources, capture it.
                    if (instance is IResourceProvider rp) _resourceProviders.Add(rp);
                }
            }
        }
    }

    private static T CreateInstance<T>(Type impl)
    {
        // Try static property "Instance" first (optional pattern)
        var prop = impl.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        if (prop?.GetValue(null) is T single) return single;

        // Fallback: parameterless constructor
        if (Activator.CreateInstance(impl) is T created) return created;

        throw new InvalidOperationException($"Cannot instantiate {impl.FullName} as {typeof(T).Name}.");
    }

    // Handlers for MCP

    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken ct)
        => ValueTask.FromResult(new ListToolsResult { Tools = _toolsByName.Values.Select(t => t.GetDefinition()).ToArray() });

    public ValueTask<CallToolResult> CallToolAsync(ModelContextProtocol.Protocol.CallToolRequestParams request, CancellationToken ct)
    {
        var name = request?.Name ?? "";
        if (!_toolsByName.TryGetValue(name, out var tool))
            throw new McpException($"Unknown tool '{name}'", McpErrorCode.InvalidRequest);

        return tool.RunAsync(request, ct);
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken ct)
    {
        var resources = _resourceProviders.SelectMany(p => p.ListResources()).ToArray();
        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    }

    public async ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        foreach (var rp in _resourceProviders)
        {
            var list = rp.ListResources();
            if (list.Any(r => r.Uri == request?.Uri))
            {
                return await rp.ReadResourceAsync(request, ct);
            }
        }

        throw new McpException($"Resource not found: {request?.Uri}", McpErrorCode.InvalidParams);
    }
}
