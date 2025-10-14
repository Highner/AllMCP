using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IArtworkSaleRepository
{
    Task<int> AddRangeIfNotExistsAsync(IEnumerable<ArtworkSale> sales, CancellationToken ct = default);
    Task<List<string>> GetCategoriesAsync(CancellationToken ct = default);
    Task<List<ArtworkSale>> GetSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default);
    Task<List<(DateTime SaleDate, decimal LowEstimate, decimal HighEstimate, decimal HammerPrice)>> GetPerformanceSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default);
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

    public async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _db.ArtworkSales
            .AsNoTracking()
            .Select(a => a.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    public async Task<List<ArtworkSale>> GetSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default)
    {
        var q = _db.ArtworkSales.AsNoTracking().Where(a => a.ArtistId == artistId);

        if (dateFrom.HasValue)
            q = q.Where(a => a.SaleDate >= dateFrom.Value);
        if (dateTo.HasValue)
            q = q.Where(a => a.SaleDate <= dateTo.Value);
        if (categories != null && categories.Count > 0)
            q = q.Where(a => categories.Contains(a.Category));

        return await q.OrderBy(a => a.SaleDate).ToListAsync(ct);
    }

    public async Task<List<(DateTime SaleDate, decimal LowEstimate, decimal HighEstimate, decimal HammerPrice)>> GetPerformanceSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default)
    {
        var q = _db.ArtworkSales.AsNoTracking()
            .Where(a => a.ArtistId == artistId && a.Sold == true && a.LowEstimate > 0 && a.HighEstimate > 0 && a.HammerPrice > 0);

        if (dateFrom.HasValue)
            q = q.Where(a => a.SaleDate >= dateFrom.Value);
        if (dateTo.HasValue)
            q = q.Where(a => a.SaleDate <= dateTo.Value);
        if (categories != null && categories.Count > 0)
            q = q.Where(a => categories.Contains(a.Category));

        var list = await q.OrderBy(a => a.SaleDate)
            .Take(1000)
            .Select(a => new { a.SaleDate, a.LowEstimate, a.HighEstimate, a.HammerPrice })
            .ToListAsync(ct);

        return list.Select(a => (a.SaleDate, a.LowEstimate, a.HighEstimate, a.HammerPrice)).ToList();
    }

    private static ArtworkSale Normalize(ArtworkSale s)
    {
        s.Name = s.Name?.Trim() ?? string.Empty;
        return s;
    }

    private static string MakeKey(string name, decimal h, decimal w, decimal hammer, DateTime saleDate, Guid artistId)
        => string.Create(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}|{h}|{w}|{hammer}|{saleDate:O}|{artistId}");
}
