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

        TasteProfile? latest = null;

        foreach (var entry in user.TasteProfiles)
        {
            if (entry is null)
            {
                continue;
            }

            if (latest is null)
            {
                latest = entry;
                continue;
            }

            var entryCreatedAt = entry.CreatedAt == default
                ? DateTime.MinValue
                : entry.CreatedAt;
            var latestCreatedAt = latest.CreatedAt == default
                ? DateTime.MinValue
                : latest.CreatedAt;

            if (entryCreatedAt > latestCreatedAt)
            {
                latest = entry;
                continue;
            }

            if (entryCreatedAt == latestCreatedAt && entry.InUse && !latest.InUse)
            {
                latest = entry;
            }
        }

        return latest;
    }

    public static (string Summary, string Profile) GetActiveTasteProfileTexts(ApplicationUser? user)
    {
        var active = GetActiveTasteProfile(user);
        var summary = active?.Summary?.Trim() ?? string.Empty;
        var profile = active?.Profile?.Trim() ?? string.Empty;
        return (summary, profile);
    }
}
