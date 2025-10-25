namespace AllMCPSolution.Models;

public class WineVintageWish
{
    public Guid Id { get; set; }

    public Guid WishlistId { get; set; }
    public Wishlist Wishlist { get; set; } = null!;

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;
}
