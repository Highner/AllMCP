namespace AllMCPSolution.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public McpToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
