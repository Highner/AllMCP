using System.ComponentModel.DataAnnotations;

namespace AllMCPSolution.Models;

public class SisterhoodInvitation
{
    public Guid Id { get; set; }

    [Required]
    public Guid SisterhoodId { get; set; }

    public Sisterhood? Sisterhood { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string InviteeEmail { get; set; } = string.Empty;

    public Guid? InviteeUserId { get; set; }

    public ApplicationUser? InviteeUser { get; set; }

    public SisterhoodInvitationStatus Status { get; set; } = SisterhoodInvitationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum SisterhoodInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Revoked = 3,
}
