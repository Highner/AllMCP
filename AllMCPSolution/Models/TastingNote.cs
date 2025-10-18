namespace AllMCPSolution.Models;

public class TastingNote
{
    public Guid Id { get; set; }

    public string TastingNote { get; set; } = string.Empty;
    public decimal? Score { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid BottleId { get; set; }
    public Bottle Bottle { get; set; } = null!;
}
