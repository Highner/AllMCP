namespace AllMCPSolution.Models;

public class SuggestedAppellation
{
    public Guid Id { get; set; }

    public Guid SubAppellationId { get; set; }
    public SubAppellation SubAppellation { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string? Reason { get; set; }

    public ICollection<SuggestedWine> SuggestedWines { get; set; } = [];
}
