namespace AllMCPSolution.Models;

public class Sisterhood
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public byte[]? ProfilePhoto { get; set; }

    public string? ProfilePhotoContentType { get; set; }

    public ICollection<SisterhoodMembership> Memberships { get; set; } = [];
    public ICollection<SisterhoodInvitation> Invitations { get; set; } = [];
    public ICollection<SipSession> SipSessions { get; set; } = [];
}
