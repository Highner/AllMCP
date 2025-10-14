namespace AllMCPSolution.Models;

public class ArtworkSale
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public decimal Height { get; set; }
    public decimal Width { get; set; }
    public int YearCreated { get; set; }
    public DateTime SaleDate { get; set; }
    public string Technique { get; set; }
    public string Category { get; set; }
    public string Currency { get; set; }
    public decimal LowEstimate { get; set; }
    public decimal HighEstimate { get; set; }
    public decimal HammerPrice { get; set; }
    public bool Sold { get; set; }
    
    // Foreign key
    public Guid ArtistId { get; set; }
    
    // Navigation property
    public Artist Artist { get; set; }
}
