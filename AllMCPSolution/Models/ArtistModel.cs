namespace AllMCPSolution.Models;

public class Artist
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    
    public ICollection<Artwork> Artworks { get; set; }

}