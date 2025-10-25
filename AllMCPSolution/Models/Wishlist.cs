namespace AllMCPSolution.Models;

public class Wishlist
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public ICollection<WineVintageWish> Wishes { get; set; } = [];
}
