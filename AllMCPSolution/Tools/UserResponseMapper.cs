using AllMCPSolution.Models;

namespace AllMCPSolution.Tools;

internal static class UserResponseMapper
{
    public static object MapUser(ApplicationUser user)
        => new
        {
            id = user.Id,
            name = user.Name,
            tasteProfileSummary = user.ActiveTasteProfile?.Summary ?? string.Empty,
            tasteProfile = user.ActiveTasteProfile?.Profile ?? string.Empty
        };
}
