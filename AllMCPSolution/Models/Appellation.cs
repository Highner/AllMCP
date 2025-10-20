namespace AllMCPSolution.Models;

public class Appellation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public Guid RegionId { get; set; }
    public Region Region { get; set; } = null!;

    public ICollection<SubAppellation> SubAppellations { get; set; } = [];
}
