namespace AllMCPSolution.Models;

public class Region
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public ICollection<Wine> Wines { get; set; } = [];
}
