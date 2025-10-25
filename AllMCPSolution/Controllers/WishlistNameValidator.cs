using System.Collections.Generic;

namespace AllMCPSolution.Controllers;

internal static class WishlistNameValidator
{
    public const int MinLength = 2;
    public const int MaxLength = 256;

    public static WishlistNameValidationResult NormalizeAndValidate(string? name, string emptyNameError)
    {
        var normalized = (name ?? string.Empty).Trim();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add(emptyNameError);
            return new WishlistNameValidationResult(string.Empty, errors);
        }

        if (normalized.Length < MinLength)
        {
            errors.Add($"Wishlist names must be at least {MinLength} characters long.");
        }

        if (normalized.Length > MaxLength)
        {
            errors.Add($"Wishlist names must be {MaxLength} characters or fewer.");
        }

        return new WishlistNameValidationResult(normalized, errors);
    }

    internal readonly record struct WishlistNameValidationResult(string NormalizedName, List<string> Errors);
}
