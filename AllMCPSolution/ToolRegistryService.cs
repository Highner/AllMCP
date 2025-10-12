using System.Reflection;
using AllMCPSolution.Attributes;
using AllMCPSolution.Tools;

namespace AllMCPSolution.Services;

public class ToolRegistry
{
    private readonly Dictionary<string, IToolBase> _tools = new();
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
            var tool = _serviceProvider.GetService(toolType) as IToolBase;
            if (tool != null)
            {
                _tools[tool.Name] = tool;
            }
        }
    }

    public IEnumerable<IToolBase> GetAllTools() => _tools.Values;

    public IToolBase? GetTool(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

    public bool HasTool(string name) => _tools.ContainsKey(name);
}
