using System.Reflection;
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Services;

public class ToolRegistry
{
    private readonly Dictionary<string, Type> _toolTypes = new();
    private readonly IServiceProvider _serviceProvider;

    public ToolRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        DiscoverTools();
    }

    private void DiscoverTools()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var toolTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpToolAttribute>() != null && 
                       typeof(IToolBase).IsAssignableFrom(t) &&
                       !t.IsAbstract &&
                       !t.IsInterface);

        foreach (var toolType in toolTypes)
        {
            var attribute = toolType.GetCustomAttribute<McpToolAttribute>();
            if (attribute != null)
            {
                _toolTypes[attribute.Name] = toolType;
            }
        }
    }

    public IEnumerable<IToolBase> GetAllTools()
    {
        using var scope = _serviceProvider.CreateScope();
        return _toolTypes.Values
            .Select(type => scope.ServiceProvider.GetService(type) as IToolBase)
            .Where(tool => tool != null)
            .ToList()!;
    }

    public IToolBase? GetTool(string name)
    {
        if (!_toolTypes.TryGetValue(name, out var toolType))
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetService(toolType) as IToolBase;
    }

    public bool HasTool(string name) => _toolTypes.ContainsKey(name);
}
