namespace AllMCPSolution.Models;

public class SuggestedWine
{
    public Guid Id { get; set; }

    public Guid SuggestedAppellationId { get; set; }
    public SuggestedAppellation SuggestedAppellation { get; set; } = null!;

    public Guid WineId { get; set; }
    public Wine Wine { get; set; } = null!;

    public string? Vintage { get; set; }
}
