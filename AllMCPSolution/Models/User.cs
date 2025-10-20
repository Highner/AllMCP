namespace AllMCPSolution.Models;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string TasteProfile { get; set; }

    public ICollection<Bottle> Bottles { get; set; } = [];
    public ICollection<TastingNote> TastingNotes { get; set; } = [];
    public ICollection<BottleLocation> BottleLocations { get; set; } = [];
    public ICollection<Sisterhood> Sisterhoods { get; set; } = [];
}
