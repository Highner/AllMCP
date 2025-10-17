namespace AllMCPSolution.Models;

public class Wine
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GrapeVariety { get; set; } = string.Empty;
    public WineColor Color { get; set; }

    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public Guid RegionId { get; set; }
    public Region Region { get; set; } = null!;

    public ICollection<Bottle> Bottles { get; set; } = [];
}
