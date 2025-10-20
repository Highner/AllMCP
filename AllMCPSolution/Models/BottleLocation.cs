namespace AllMCPSolution.Models;

public class BottleLocation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Bottle> Bottles { get; set; } = [];
}
