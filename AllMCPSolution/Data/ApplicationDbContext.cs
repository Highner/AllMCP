using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Models;

namespace AllMCPSolution.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
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
    public DbSet<SubAppellation> SubAppellations { get; set; }
    public DbSet<Wine> Wines { get; set; }
    public DbSet<WineVintage> WineVintages { get; set; }
    public DbSet<WineVintageEvolutionScore> WineVintageEvolutionScores { get; set; }
    public DbSet<Bottle> Bottles { get; set; }
    public DbSet<BottleLocation> BottleLocations { get; set; }
    public DbSet<TastingNote> TastingNotes { get; set; }
    public DbSet<Sisterhood> Sisterhoods { get; set; }
    public DbSet<SisterhoodMembership> SisterhoodMemberships { get; set; }
    public DbSet<SisterhoodInvitation> SisterhoodInvitations { get; set; }
    public DbSet<SipSession> SipSessions { get; set; }
    public DbSet<SipSessionBottle> SipSessionBottles { get; set; }
    public DbSet<WineSurferNotificationDismissal> WineSurferNotificationDismissals { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.Name)
                .HasMaxLength(256);

            entity.Property(u => u.TasteProfile)
                .HasMaxLength(2048);
        });

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

            e.HasOne(w => w.SubAppellation)
                .WithMany(sa => sa.Wines)
                .HasForeignKey(w => w.SubAppellationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(w => new { w.Name, w.SubAppellationId })
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

        modelBuilder.Entity<WineVintageEvolutionScore>(e =>
        {
            e.Property(ev => ev.Score).HasColumnType("decimal(18,2)");
            e.Property(ev => ev.Year).IsRequired();

            e.HasOne(ev => ev.WineVintage)
                .WithMany(wv => wv.EvolutionScores)
                .HasForeignKey(ev => ev.WineVintageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ev => new { ev.WineVintageId, ev.Year })
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

        modelBuilder.Entity<SubAppellation>(e =>
        {
            e.Property(sa => sa.Name).HasMaxLength(256);

            e.HasOne(sa => sa.Appellation)
                .WithMany(a => a.SubAppellations)
                .HasForeignKey(sa => sa.AppellationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(sa => new { sa.Name, sa.AppellationId })
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

            e.HasOne(b => b.BottleLocation)
                .WithMany(bl => bl.Bottles)
                .HasForeignKey(b => b.BottleLocationId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(b => b.User)
                .WithMany(u => u.Bottles)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BottleLocation>(e =>
        {
            e.Property(bl => bl.Name)
                .HasMaxLength(128)
                .IsRequired();

            e.HasOne(bl => bl.User)
                .WithMany(u => u.BottleLocations)
                .HasForeignKey(bl => bl.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(bl => new { bl.UserId, bl.Name })
                .IsUnique();
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

        modelBuilder.Entity<Sisterhood>(e =>
        {
            e.Property(s => s.Name)
                .HasMaxLength(256)
                .IsRequired();

            e.HasIndex(s => s.Name)
                .IsUnique();
        });

        modelBuilder.Entity<SipSession>(entity =>
        {
            entity.Property(session => session.Name)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(session => session.Description)
                .HasMaxLength(2048);

            entity.Property(session => session.Location)
                .HasMaxLength(256);

            entity.Property(session => session.ScheduledAt)
                .HasColumnType("datetime2");

            entity.Property(session => session.CreatedAt)
                .HasColumnType("datetime2");

            entity.Property(session => session.UpdatedAt)
                .HasColumnType("datetime2");

            entity.HasIndex(session => new { session.SisterhoodId, session.ScheduledAt });

            entity.HasOne(session => session.Sisterhood)
                .WithMany(sisterhood => sisterhood.SipSessions)
                .HasForeignKey(session => session.SisterhoodId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(session => session.Bottles)
                .WithOne(link => link.SipSession)
                .HasForeignKey(link => link.SipSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SipSessionBottle>(entity =>
        {
            entity.ToTable("BottleSipSession");

            entity.HasKey(link => new { link.BottleId, link.SipSessionId });

            entity.Property(link => link.IsRevealed)
                .HasDefaultValue(false);

            entity.HasIndex(link => link.SipSessionId);

            entity.HasOne(link => link.Bottle)
                .WithMany(bottle => bottle.SipSessions)
                .HasForeignKey(link => link.BottleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(link => link.SipSession)
                .WithMany(session => session.Bottles)
                .HasForeignKey(link => link.SipSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SisterhoodMembership>(entity =>
        {
            entity.ToTable("UserSisterhoods");

            entity.HasKey(membership => new { membership.UserId, membership.SisterhoodId });

            entity.Property(membership => membership.JoinedAt)
                .HasColumnType("datetime2");

            entity.Property(membership => membership.IsAdmin)
                .HasDefaultValue(false);

            entity.HasIndex(membership => new { membership.SisterhoodId, membership.UserId })
                .IsUnique();

            entity.HasOne(membership => membership.Sisterhood)
                .WithMany(sisterhood => sisterhood.Memberships)
                .HasForeignKey(membership => membership.SisterhoodId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(membership => membership.User)
                .WithMany(user => user.SisterhoodMemberships)
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SisterhoodInvitation>(entity =>
        {
            entity.Property(invite => invite.InviteeEmail)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(invite => invite.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(invite => invite.CreatedAt)
                .HasColumnType("datetime2");

            entity.Property(invite => invite.UpdatedAt)
                .HasColumnType("datetime2");

            entity.HasIndex(invite => new { invite.SisterhoodId, invite.InviteeEmail })
                .IsUnique();

            entity.HasOne(invite => invite.Sisterhood)
                .WithMany(sisterhood => sisterhood.Invitations)
                .HasForeignKey(invite => invite.SisterhoodId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(invite => invite.InviteeUser)
                .WithMany(user => user.SisterhoodInvitations)
                .HasForeignKey(invite => invite.InviteeUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WineSurferNotificationDismissal>(entity =>
        {
            entity.Property(dismissal => dismissal.Category)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(dismissal => dismissal.Stamp)
                .HasMaxLength(512)
                .IsRequired();

            entity.Property(dismissal => dismissal.DismissedAtUtc)
                .HasColumnType("datetime2");

            entity.HasIndex(dismissal => new { dismissal.UserId, dismissal.Category, dismissal.Stamp })
                .IsUnique();

            entity.HasOne(dismissal => dismissal.User)
                .WithMany(user => user.NotificationDismissals)
                .HasForeignKey(dismissal => dismissal.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
