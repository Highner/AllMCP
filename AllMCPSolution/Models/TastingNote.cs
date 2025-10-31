namespace AllMCPSolution.Models;

public class TastingNote
{
    public Guid Id { get; set; }

    public string Note { get; set; } = string.Empty;
    public decimal? Score { get; set; }

    // When true, this note indicates the bottle was not actually tasted; Note and Score should be empty/null.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool NotTasted { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid BottleId { get; set; }
    public Bottle Bottle { get; set; } = null!;
}
