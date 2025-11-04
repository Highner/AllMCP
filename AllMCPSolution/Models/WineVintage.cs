namespace AllMCPSolution.Models;

public class WineVintage
{
    public Guid Id { get; set; }

    public Guid WineId { get; set; }
    public Wine Wine { get; set; } = null!;

    public int Vintage { get; set; }

    public ICollection<Bottle> Bottles { get; set; } = [];

    public ICollection<WineVintageEvolutionScore> EvolutionScores { get; set; } = [];
    public ICollection<WineVintageWish> Wishes { get; set; } = [];
    public ICollection<WineVintageUserDrinkingWindow> DrinkingWindows { get; set; } = [];
}
