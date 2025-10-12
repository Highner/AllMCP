public class Artwork
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Height { get; set; }
    public int Width { get; set; }
    public int YearCreated { get; set; }
    
    // Foreign key
    public Guid ArtistId { get; set; }
    
    // Navigation property
    public Artist Artist { get; set; }
}
