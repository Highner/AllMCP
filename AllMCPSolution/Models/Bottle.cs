namespace AllMCPSolution.Models;

public class Bottle
{
    public Guid Id { get; set; }
    public decimal? Price { get; set; }

    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }

    public bool PendingDelivery { get; set; }

    public Guid WineVintageId { get; set; }
    public WineVintage WineVintage { get; set; } = null!;

    public Guid? BottleLocationId { get; set; }
    public BottleLocation? BottleLocation { get; set; }

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public ICollection<TastingNote> TastingNotes { get; set; } = [];

    public ICollection<SipSessionBottle> SipSessions { get; set; } = [];
    public ICollection<BottleShare> Shares { get; set; } = [];
}
