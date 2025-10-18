namespace AllMCPSolution.Models;

public class Bottle
{
    public Guid Id { get; set; }
    public decimal? Price { get; set; }

    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public ICollection<TastingNote> TastingNotes { get; set; } = [];
}
