using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Artworks; // ArtworkSale, Artist
using AllMCPSolution;          // ApplicationDbContext

public interface IArtworkSaleRepository
{
    Task<int> AddRangeIfNotExistsAsync(IEnumerable<ArtworkSale> sales, CancellationToken ct = default);
}

public class ArtworkSaleRepository : IArtworkSaleRepository
{
    private readonly ApplicationDbContext _db;
    public ArtworkSaleRepository(ApplicationDbContext db) => _db = db;

    public async Task<int> AddRangeIfNotExistsAsync(IEnumerable<ArtworkSale> sales, CancellationToken ct = default)
    {
        if (sales is null) throw new ArgumentNullException(nameof(sales));

        var incoming = sales.Where(s => s != null).Select(Normalize).ToList();
        if (incoming.Count == 0) return 0;

        var nameSet     = incoming.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var artistSet   = incoming.Select(s => s.ArtistId).ToHashSet();
        var dateSet     = incoming.Select(s => s.SaleDate).ToHashSet();
        var heightSet   = incoming.Select(s => s.Height).ToHashSet();
        var widthSet    = incoming.Select(s => s.Width).ToHashSet();
        var hammerSet   = incoming.Select(s => s.HammerPrice).ToHashSet();

        var existing = await _db.ArtworkSales
            .AsNoTracking()
            .Where(a => artistSet.Contains(a.ArtistId)
                        && nameSet.Contains(a.Name)
                        && dateSet.Contains(a.SaleDate)
                        && heightSet.Contains(a.Height)
                        && widthSet.Contains(a.Width)
                        && hammerSet.Contains(a.HammerPrice))
            .Select(a => new { a.Name, a.Height, a.Width, a.HammerPrice, a.SaleDate, a.ArtistId })
            .ToListAsync(ct);

        var existingKeys = new HashSet<string>(
            existing.Select(k => MakeKey(k.Name, k.Height, k.Width, k.HammerPrice, k.SaleDate, k.ArtistId)));

        var toInsert = incoming
            .Where(s => !existingKeys.Contains(MakeKey(s.Name, s.Height, s.Width, s.HammerPrice, s.SaleDate, s.ArtistId)))
            .ToList();

        if (toInsert.Count == 0) return 0;

        await _db.ArtworkSales.AddRangeAsync(toInsert, ct);
        return await _db.SaveChangesAsync(ct);
    }

    private static ArtworkSale Normalize(ArtworkSale s)
    {
        s.Name = s.Name?.Trim() ?? string.Empty;
        return s;
    }

    private static string MakeKey(string name, decimal h, decimal w, decimal hammer, DateTime saleDate, Guid artistId)
        => string.Create(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}|{h}|{w}|{hammer}|{saleDate:O}|{artistId}");
}
