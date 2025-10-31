namespace AllMCPSolution.Models;

public class BottleShare
{
    public Guid Id { get; set; }

    public Guid BottleId { get; set; }
    public Bottle Bottle { get; set; } = null!;

    public Guid SharedByUserId { get; set; }
    public ApplicationUser SharedByUser { get; set; } = null!;

    public Guid SharedWithUserId { get; set; }
    public ApplicationUser SharedWithUser { get; set; } = null!;

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}
