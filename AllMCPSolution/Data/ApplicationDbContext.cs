using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Models;

namespace AllMCPSolution.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Artist> Artists { get; set; }
    public DbSet<Artwork> Artworks { get; set; }
    public DbSet<ArtworkSale> ArtworkSales { get; set; }
    public DbSet<InflationIndex> InflationIndices { get; set; }
    public DbSet<Country> Countries { get; set; }
    public DbSet<Region> Regions { get; set; }
    public DbSet<Wine> Wines { get; set; }
    public DbSet<WineVintage> WineVintages { get; set; }
    public DbSet<Bottle> Bottles { get; set; }

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
            e.Property(p => p.Name).HasMaxLength(256);

            // Prevent duplicates: (Name, Height, Width, HammerPrice, SaleDate, ArtistId)
            e.HasIndex(p => new { p.Name, p.Height, p.Width, p.HammerPrice, p.SaleDate, p.ArtistId })
                .IsUnique();
        });

        modelBuilder.Entity<InflationIndex>(e =>
        {
            e.HasIndex(i => new { i.Year, i.Month }).IsUnique();
            e.Property(i => i.IndexValue).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<Wine>(e =>
        {
            e.Property(w => w.Name).HasMaxLength(256);
            e.Property(w => w.GrapeVariety).HasMaxLength(256);

            e.HasOne(w => w.Country)
                .WithMany(c => c.Wines)
                .HasForeignKey(w => w.CountryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(w => w.Region)
                .WithMany(r => r.Wines)
                .HasForeignKey(w => w.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(w => new { w.Name, w.CountryId, w.RegionId })
                .IsUnique();
        });

        modelBuilder.Entity<WineVintage>(e =>
        {
            e.Property(wv => wv.Vintage).IsRequired();

            e.HasOne(wv => wv.Wine)
                .WithMany(w => w.WineVintages)
                .HasForeignKey(wv => wv.WineId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(wv => new { wv.WineId, wv.Vintage })
                .IsUnique();
        });

        modelBuilder.Entity<Country>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(128);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Region>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(128);
            e.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<Bottle>(e =>
        {
            e.Property(b => b.TastingNote).HasMaxLength(1024);

            e.HasOne(b => b.WineVintage)
                .WithMany(wv => wv.Bottles)
                .HasForeignKey(b => b.WineVintageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
