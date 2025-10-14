using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Artist> Artists { get; set; }
    public DbSet<Artwork> Artworks { get; set; }
    public DbSet<ArtworkSale> ArtworkSales { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<ArtworkSale>(e =>
        {
            e.Property(p => p.Height).HasColumnType("decimal(18,4)");
            e.Property(p => p.Width).HasColumnType("decimal(18,4)");
            e.Property(p => p.LowEstimate).HasColumnType("decimal(18,2)");
            e.Property(p => p.HighEstimate).HasColumnType("decimal(18,2)");
            e.Property(p => p.HammerPrice).HasColumnType("decimal(18,2)");

            // Prevent duplicates: (Name, Height, Width, HammerPrice, SaleDate, ArtistId)
            e.HasIndex(p => new { p.Name, p.Height, p.Width, p.HammerPrice, p.SaleDate, p.ArtistId })
                .IsUnique();
        });

        

    }
}
