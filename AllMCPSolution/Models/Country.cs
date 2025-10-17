namespace AllMCPSolution.Models;

public class Country
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Wine> Wines { get; set; } = [];
}
