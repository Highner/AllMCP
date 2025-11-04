namespace AllMCPSolution.Models;

public class WineVintageUserDrinkingWindow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public DateTime StartingDate { get; set; }

    public DateTime EndDate { get; set; }
}
