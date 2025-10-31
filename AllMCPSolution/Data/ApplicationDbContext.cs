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

    public DbSet<Country> Countries { get; set; }
    public DbSet<Region> Regions { get; set; }
    public DbSet<Appellation> Appellations { get; set; }
    public DbSet<SubAppellation> SubAppellations { get; set; }
    public DbSet<SuggestedAppellation> SuggestedAppellations { get; set; }
    public DbSet<SuggestedWine> SuggestedWines { get; set; }
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
    public DbSet<TasteProfile> TasteProfiles { get; set; }
    public DbSet<Wishlist> Wishlists { get; set; }
    public DbSet<WineVintageWish> WineVintageWishes { get; set; }
    public DbSet<BottleShare> BottleShares { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.Name)
                .HasMaxLength(256);

            entity.Property(u => u.IsAdmin)
                .HasDefaultValue(false);

            entity.Property(u => u.SuggestionBudget)
                .HasColumnType("decimal(10,2)");

            entity.Property(u => u.ProfilePhoto)
                .HasColumnType("varbinary(max)");

            entity.Property(u => u.ProfilePhotoContentType)
                .HasMaxLength(128);
        });

        modelBuilder.Entity<Sisterhood>(entity =>
        {
            entity.Property(s => s.ProfilePhoto)
                .HasColumnType("varbinary(max)");

            entity.Property(s => s.ProfilePhotoContentType)
                .HasMaxLength(128);
        });

        modelBuilder.Entity<TasteProfile>(entity =>
        {
            entity.Property(profile => profile.Profile)
                .HasMaxLength(4096);

            entity.Property(profile => profile.Summary)
                .HasMaxLength(512);

            entity.Property(profile => profile.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(profile => profile.User)
                .WithMany(user => user.TasteProfiles)
                .HasForeignKey(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(profile => new { profile.UserId, profile.InUse })
                .HasFilter("[InUse] = 1")
                .IsUnique();
        });

        foreach (var property in modelBuilder.Model
                     .GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
        {
            property.SetColumnType("datetime2");
        }

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

            e.HasOne(ev => ev.User)
                .WithMany(u => u.WineVintageEvolutionScores)
                .HasForeignKey(ev => ev.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ev => ev.WineVintage)
                .WithMany(wv => wv.EvolutionScores)
                .HasForeignKey(ev => ev.WineVintageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ev => new { ev.UserId, ev.WineVintageId, ev.Year })
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

        modelBuilder.Entity<SuggestedAppellation>(e =>
        {
            e.Property(suggested => suggested.Reason)
                .HasMaxLength(512);

            e.HasOne(suggested => suggested.SubAppellation)
                .WithMany(subAppellation => subAppellation.SuggestedAppellations)
                .HasForeignKey(suggested => suggested.SubAppellationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(suggested => suggested.TasteProfile)
                .WithMany(profile => profile.SuggestedAppellations)
                .HasForeignKey(suggested => suggested.TasteProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(suggested => new { suggested.TasteProfileId, suggested.SubAppellationId })
                .IsUnique();
        });

        modelBuilder.Entity<SuggestedWine>(e =>
        {
            e.Property(wine => wine.Vintage)
                .HasMaxLength(32);

            e.HasOne(wine => wine.SuggestedAppellation)
                .WithMany(appellation => appellation.SuggestedWines)
                .HasForeignKey(wine => wine.SuggestedAppellationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(wine => wine.Wine)
                .WithMany(existing => existing.SuggestedWines)
                .HasForeignKey(wine => wine.WineId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(wine => new { wine.SuggestedAppellationId, wine.WineId })
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

        modelBuilder.Entity<BottleShare>(entity =>
        {
            entity.Property(share => share.SharedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(share => new { share.BottleId, share.SharedWithUserId })
                .IsUnique();

            entity.HasOne(share => share.Bottle)
                .WithMany(bottle => bottle.Shares)
                .HasForeignKey(share => share.BottleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(share => share.SharedByUser)
                .WithMany(user => user.GrantedBottleShares)
                .HasForeignKey(share => share.SharedByUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(share => share.SharedWithUser)
                .WithMany(user => user.ReceivedBottleShares)
                .HasForeignKey(share => share.SharedWithUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BottleLocation>(e =>
        {
            e.Property(bl => bl.Name)
                .HasMaxLength(128)
                .IsRequired();

            e.Property(bl => bl.Capacity)
                .HasColumnType("int")
                .IsRequired(false);

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

            entity.Property(session => session.FoodSuggestion)
                .HasMaxLength(4096);

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

        modelBuilder.Entity<Wishlist>(entity =>
        {
            entity.Property(wishlist => wishlist.Name)
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(wishlist => new { wishlist.UserId, wishlist.Name })
                .IsUnique();

            entity.HasOne(wishlist => wishlist.User)
                .WithMany(user => user.Wishlists)
                .HasForeignKey(wishlist => wishlist.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WineVintageWish>(entity =>
        {
            entity.HasIndex(wish => new { wish.WishlistId, wish.WineVintageId })
                .IsUnique();

            entity.HasOne(wish => wish.Wishlist)
                .WithMany(wishlist => wishlist.Wishes)
                .HasForeignKey(wish => wish.WishlistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(wish => wish.WineVintage)
                .WithMany(wineVintage => wineVintage.Wishes)
                .HasForeignKey(wish => wish.WineVintageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
