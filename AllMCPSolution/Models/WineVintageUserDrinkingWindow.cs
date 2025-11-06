namespace AllMCPSolution.Models;

public class WineVintageUserDrinkingWindow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public int StartingYear { get; set; }

    public int EndingYear { get; set; }

    public string? Explanation { get; set; }
}
