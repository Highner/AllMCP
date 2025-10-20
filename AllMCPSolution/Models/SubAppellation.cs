namespace AllMCPSolution.Models;

public class SubAppellation
{
    public Guid Id { get; set; }
    public string? Name { get; set; }

    public Guid AppellationId { get; set; }
    public Appellation Appellation { get; set; } = null!;

    public ICollection<Wine> Wines { get; set; } = [];
}
