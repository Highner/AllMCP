namespace AllMCPSolution.Models;

public class Wine
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GrapeVariety { get; set; } = string.Empty;
    public WineColor Color { get; set; }

    public Guid SubAppellationId { get; set; }
    public SubAppellation SubAppellation { get; set; } = null!;

    public ICollection<WineVintage> WineVintages { get; set; } = [];
    public ICollection<SuggestedWine> SuggestedWines { get; set; } = [];
}
