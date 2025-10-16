using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AllMCPSolution.Data;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public interface IArtworkSaleRepository
{
    // Existing batch and query helpers
    Task<int> AddRangeIfNotExistsAsync(IEnumerable<ArtworkSale> sales, CancellationToken ct = default);
    Task<List<string>> GetCategoriesAsync(CancellationToken ct = default);
    Task<List<ArtworkSale>> GetSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default);
    Task<List<ArtworkSale>> GetPerformanceSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default);

    // CRUD for ArtworkSales maintenance
    Task<List<ArtworkSale>> GetAllAsync(string? search, string? sortBy, bool desc, int take, CancellationToken ct = default);
    Task<ArtworkSale?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ArtworkSale> AddAsync(ArtworkSale sale, CancellationToken ct = default);
    Task<ArtworkSale?> UpdateAsync(Guid id, ArtworkSale input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
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

    public async Task<List<ArtworkSale>> GetPerformanceSalesAsync(Guid artistId, DateTime? dateFrom, DateTime? dateTo, List<string> categories, CancellationToken ct = default)
    {
        var q = _db.ArtworkSales.AsNoTracking()
            .Where(a => a.ArtistId == artistId && a.Sold == true && a.LowEstimate > 0 && a.HighEstimate > 0 && a.HammerPrice > 0);

        if (dateFrom.HasValue)
            q = q.Where(a => a.SaleDate >= dateFrom.Value);
        if (dateTo.HasValue)
            q = q.Where(a => a.SaleDate <= dateTo.Value);
        if (categories != null && categories.Count > 0)
            q = q.Where(a => categories.Contains(a.Category));

        return await q.OrderBy(a => a.SaleDate)
            .Take(1000)
            .ToListAsync(ct);
    }

    // CRUD methods used by maintenance UI
    public async Task<List<ArtworkSale>> GetAllAsync(string? search, string? sortBy, bool desc, int take, CancellationToken ct = default)
    {
        var q = _db.ArtworkSales.AsNoTracking().Include(a => a.Artist).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a =>
                (a.Name != null && a.Name.Contains(s)) ||
                (a.Technique != null && a.Technique.Contains(s)) ||
                (a.Category != null && a.Category.Contains(s)) ||
                (a.Currency != null && a.Currency.Contains(s)) ||
                (a.Artist != null && (
                    (a.Artist.FirstName != null && a.Artist.FirstName.Contains(s)) ||
                    (a.Artist.LastName != null && a.Artist.LastName.Contains(s))
                ))
            );
        }

        q = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? q.OrderByDescending(a => a.Name) : q.OrderBy(a => a.Name),
            "saledate" => desc ? q.OrderByDescending(a => a.SaleDate) : q.OrderBy(a => a.SaleDate),
            "hammerprice" => desc ? q.OrderByDescending(a => a.HammerPrice) : q.OrderBy(a => a.HammerPrice),
            "category" => desc ? q.OrderByDescending(a => a.Category) : q.OrderBy(a => a.Category),
            "artist" => desc ? q.OrderByDescending(a => a.Artist!.LastName).ThenByDescending(a => a.Artist!.FirstName)
                               : q.OrderBy(a => a.Artist!.LastName).ThenBy(a => a.Artist!.FirstName),
            _ => q.OrderBy(a => a.SaleDate)
        };

        if (take <= 0 || take > 5000) take = 5000; // safety cap
        return await q.Take(take).ToListAsync(ct);
    }

    public async Task<ArtworkSale?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<ArtworkSale> AddAsync(ArtworkSale sale, CancellationToken ct = default)
    {
        if (sale == null) throw new ArgumentNullException(nameof(sale));
        sale.Id = sale.Id == Guid.Empty ? Guid.NewGuid() : sale.Id;
        _db.ArtworkSales.Add(sale);
        await _db.SaveChangesAsync(ct);
        return sale;
    }

    public async Task<ArtworkSale?> UpdateAsync(Guid id, ArtworkSale input, CancellationToken ct = default)
    {
        var entity = await _db.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity == null) return null;

        entity.Name = input.Name;
        entity.Height = input.Height;
        entity.Width = input.Width;
        entity.YearCreated = input.YearCreated;
        entity.SaleDate = input.SaleDate;
        entity.Technique = input.Technique;
        entity.Category = input.Category;
        entity.Currency = input.Currency;
        entity.LowEstimate = input.LowEstimate;
        entity.HighEstimate = input.HighEstimate;
        entity.HammerPrice = input.HammerPrice;
        entity.Sold = input.Sold;
        entity.ArtistId = input.ArtistId;

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ArtworkSales.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity == null) return false;
        _db.ArtworkSales.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ArtworkSale Normalize(ArtworkSale s)
    {
        s.Name = s.Name?.Trim() ?? string.Empty;
        return s;
    }

    private static string MakeKey(string name, decimal h, decimal w, decimal hammer, DateTime saleDate, Guid artistId)
        => string.Create(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}|{h}|{w}|{hammer}|{saleDate:O}|{artistId}");
}
