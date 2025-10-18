namespace AllMCPSolution.Models;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string TasteProfile { get; set; }

    public ICollection<TastingNote> TastingNotes { get; set; } = [];
}
