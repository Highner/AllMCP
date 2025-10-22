namespace AllMCPSolution.Models;

public class SipSessionBottle
{
    public Guid SipSessionId { get; set; }
    public SipSession? SipSession { get; set; }

    public Guid BottleId { get; set; }
    public Bottle Bottle { get; set; } = null!;

    public bool IsRevealed { get; set; }
}
