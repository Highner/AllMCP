using System.ComponentModel.DataAnnotations;

namespace AllMCPSolution.Models;

public class SipSession
{
    public Guid Id { get; set; }

    [Required]
    public Guid SisterhoodId { get; set; }

    public Sisterhood? Sisterhood { get; set; }

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? Description { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
