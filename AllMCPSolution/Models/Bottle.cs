namespace AllMCPSolution.Models;

public class Bottle
{
    public Guid Id { get; set; }
    public string TastingNote { get; set; } = string.Empty;

    public Guid WineId { get; set; }
    public Wine Wine { get; set; } = null!;
}
