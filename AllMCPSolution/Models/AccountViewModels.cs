using System.ComponentModel.DataAnnotations;

namespace AllMCPSolution.Models;

public class ExternalLoginOption
{
    public string AuthenticationScheme { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Stay signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    public IReadOnlyList<ExternalLoginOption> ExternalLogins { get; set; } = Array.Empty<ExternalLoginOption>();
}

public class RegisterViewModel
{
    [Display(Name = "Full name")]
    [StringLength(128)]
    public string? Name { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "The {0} must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public IReadOnlyList<ExternalLoginOption> ExternalLogins { get; set; } = Array.Empty<ExternalLoginOption>();
}

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class ResetPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "The {0} must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string OldPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "The {0} must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UpdateAccountDisplayNameViewModel
{
    [Required]
    [StringLength(128, MinimumLength = 2, ErrorMessage = "Display name must be between {2} and {1} characters long.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;
}

public class PasswordResetNotificationViewModel
{
    public string? Email { get; set; }

    public string? ResetLink { get; set; }
}
