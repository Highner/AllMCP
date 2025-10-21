namespace AllMCPSolution.Models;

public class BottleLocation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public ICollection<Bottle> Bottles { get; set; } = [];
}
