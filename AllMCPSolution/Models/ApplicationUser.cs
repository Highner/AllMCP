using Microsoft.AspNetCore.Identity;

namespace AllMCPSolution.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public decimal? SuggestionBudget { get; set; }

    public ICollection<Bottle> Bottles { get; set; } = [];
    public ICollection<TastingNote> TastingNotes { get; set; } = [];
    public ICollection<BottleLocation> BottleLocations { get; set; } = [];
    public ICollection<SisterhoodMembership> SisterhoodMemberships { get; set; } = [];
    public ICollection<SisterhoodInvitation> SisterhoodInvitations { get; set; } = [];
    public ICollection<WineSurferNotificationDismissal> NotificationDismissals { get; set; } = [];
    public ICollection<WineVintageEvolutionScore> WineVintageEvolutionScores { get; set; } = [];
    public ICollection<TasteProfile> TasteProfiles { get; set; } = [];
    public ICollection<Wishlist> Wishlists { get; set; } = [];
}
