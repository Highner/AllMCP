using System.ComponentModel.DataAnnotations;

namespace AllMCPSolution.Models;

public class SisterhoodMembership
{
    [Required]
    public Guid SisterhoodId { get; set; }

    public Sisterhood? Sisterhood { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public bool IsAdmin { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
