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
    public DbSet<Appellation> Appellations { get; set; }
    public DbSet<Wine> Wines { get; set; }
    public DbSet<WineVintage> WineVintages { get; set; }
    public DbSet<Bottle> Bottles { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<TastingNote> TastingNotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var property in modelBuilder.Model
                     .GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
        {
            property.SetColumnType("datetime2");
        }

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

            e.HasOne(w => w.Appellation)
                .WithMany(a => a.Wines)
                .HasForeignKey(w => w.AppellationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(w => new { w.Name, w.AppellationId })
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

        modelBuilder.Entity<Appellation>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(256);

            e.HasOne(a => a.Region)
                .WithMany(r => r.Appellations)
                .HasForeignKey(a => a.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.Name, a.RegionId })
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

            e.HasOne(r => r.Country)
                .WithMany(c => c.Regions)
                .HasForeignKey(r => r.CountryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.Name, r.CountryId })
                .IsUnique();
        });

        modelBuilder.Entity<Bottle>(e =>
        {
            e.Property(b => b.IsDrunk).IsRequired();

            e.HasOne(b => b.WineVintage)
                .WithMany(wv => wv.Bottles)
                .HasForeignKey(b => b.WineVintageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TastingNote>(e =>
        {
            e.Property(tn => tn.Note).HasMaxLength(2048);
            e.Property(tn => tn.Score).HasColumnType("decimal(18,2)");

            e.HasOne(tn => tn.Bottle)
                .WithMany(b => b.TastingNotes)
                .HasForeignKey(tn => tn.BottleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(tn => tn.User)
                .WithMany(u => u.TastingNotes)
                .HasForeignKey(tn => tn.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
