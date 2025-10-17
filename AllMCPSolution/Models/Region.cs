namespace AllMCPSolution.Models;

public class Region
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Wine> Wines { get; set; } = [];
}
