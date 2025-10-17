namespace AllMCPSolution.Models;

public class Bottle
{
    public Guid Id { get; set; }
    public string TastingNote { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public decimal? Score { get; set; }

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;
}
