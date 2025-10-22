namespace AllMCPSolution.Models;

public class WineVintageEvolutionScore
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public int Year { get; set; }

    public decimal Score { get; set; }
}
