namespace AllMCPSolution.Models;

public class TasteProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string Profile { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    public bool InUse { get; set; }
        = false;

    public ICollection<SuggestedAppellation> SuggestedAppellations { get; set; } = [];
}
