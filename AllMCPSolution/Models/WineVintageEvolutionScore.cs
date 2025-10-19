namespace AllMCPSolution.Models;

public class WineVintageEvolutionScore
{
    public Guid Id { get; set; }

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public int Year { get; set; }

    public decimal Score { get; set; }
}
