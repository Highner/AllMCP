using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.AspNetCore.Identity;

namespace AllMCPSolution.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public ICollection<Bottle> Bottles { get; set; } = [];
    public ICollection<TastingNote> TastingNotes { get; set; } = [];
    public ICollection<BottleLocation> BottleLocations { get; set; } = [];
    public ICollection<SisterhoodMembership> SisterhoodMemberships { get; set; } = [];
    public ICollection<SisterhoodInvitation> SisterhoodInvitations { get; set; } = [];
    public ICollection<WineSurferNotificationDismissal> NotificationDismissals { get; set; } = [];
    public ICollection<WineVintageEvolutionScore> WineVintageEvolutionScores { get; set; } = [];
    public ICollection<SuggestedAppellation> SuggestedAppellations { get; set; } = [];
    public ICollection<TasteProfile> TasteProfiles { get; set; } = [];

    [NotMapped]
    public TasteProfile? ActiveTasteProfile
    {
        get
        {
            if (TasteProfiles is null || TasteProfiles.Count == 0)
            {
                return null;
            }

            var active = TasteProfiles.FirstOrDefault(tp => tp.InUse);
            if (active is not null)
            {
                return active;
            }

            return TasteProfiles
                .OrderByDescending(tp => tp.CreatedAt)
                .FirstOrDefault();
        }
    }
}
