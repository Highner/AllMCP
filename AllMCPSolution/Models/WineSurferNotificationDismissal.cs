using System;

namespace AllMCPSolution.Models;

public class WineSurferNotificationDismissal
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Stamp { get; set; } = string.Empty;

    public DateTime DismissedAtUtc { get; set; }

    public ApplicationUser User { get; set; } = null!;
}
