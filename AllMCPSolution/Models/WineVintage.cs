namespace AllMCPSolution.Models;

public class WineVintage
{
    public Guid Id { get; set; }
    public int Vintage { get; set; }

    public Guid WineId { get; set; }
    public Wine Wine { get; set; } = null!;

    public ICollection<Bottle> Bottles { get; set; } = [];
}
