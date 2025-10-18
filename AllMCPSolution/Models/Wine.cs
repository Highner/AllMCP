namespace AllMCPSolution.Models;

public class Wine
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GrapeVariety { get; set; } = string.Empty;
    public WineColor Color { get; set; }

    public Guid AppellationId { get; set; }
    public Appellation Appellation { get; set; } = null!;

    public ICollection<WineVintage> WineVintages { get; set; } = [];
}
