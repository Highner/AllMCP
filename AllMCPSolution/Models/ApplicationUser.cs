using Microsoft.AspNetCore.Identity;

namespace AllMCPSolution.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string TasteProfile { get; set; } = string.Empty;
    public string TasteProfileSummary { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public ICollection<Bottle> Bottles { get; set; } = [];
    public ICollection<TastingNote> TastingNotes { get; set; } = [];
    public ICollection<BottleLocation> BottleLocations { get; set; } = [];
    public ICollection<SisterhoodMembership> SisterhoodMemberships { get; set; } = [];
    public ICollection<SisterhoodInvitation> SisterhoodInvitations { get; set; } = [];
    public ICollection<WineSurferNotificationDismissal> NotificationDismissals { get; set; } = [];
}
