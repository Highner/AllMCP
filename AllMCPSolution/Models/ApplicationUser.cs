using Microsoft.AspNetCore.Identity;

namespace AllMCPSolution.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string TasteProfile { get; set; } = string.Empty;

    public ICollection<Bottle> Bottles { get; set; } = [];
    public ICollection<TastingNote> TastingNotes { get; set; } = [];
    public ICollection<BottleLocation> BottleLocations { get; set; } = [];
    public ICollection<Sisterhood> Sisterhoods { get; set; } = [];
}
