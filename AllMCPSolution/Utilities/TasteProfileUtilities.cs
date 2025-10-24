using AllMCPSolution.Models;

namespace AllMCPSolution.Utilities;

public static class TasteProfileUtilities
{
    public static TasteProfile? GetActiveTasteProfile(ApplicationUser? user)
    {
        if (user?.TasteProfiles is null || user.TasteProfiles.Count == 0)
        {
            return null;
        }

        TasteProfile? active = null;

        foreach (var entry in user.TasteProfiles)
        {
            if (entry is null)
            {
                continue;
            }

            if (entry.InUse)
            {
                if (active is null || !active.InUse || entry.CreatedAt > active.CreatedAt)
                {
                    active = entry;
                }

                continue;
            }

            if (active is null || (!active.InUse && entry.CreatedAt > active.CreatedAt))
            {
                active = entry;
            }
        }

        return active;
    }

    public static (string Summary, string Profile) GetActiveTasteProfileTexts(ApplicationUser? user)
    {
        var active = GetActiveTasteProfile(user);
        var summary = active?.Summary?.Trim() ?? string.Empty;
        var profile = active?.Profile?.Trim() ?? string.Empty;
        return (summary, profile);
    }
}
