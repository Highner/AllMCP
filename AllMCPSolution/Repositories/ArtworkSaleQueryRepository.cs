using AllMCPSolution.Data;
using AllMCPSolution.Models;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Repositories;

public interface IArtworkSaleQueryRepository
{
    IQueryable<ArtworkSale> ArtworkSales { get; }
}

public class ArtworkSaleQueryRepository : IArtworkSaleQueryRepository
{
    private readonly ApplicationDbContext _db;
    public ArtworkSaleQueryRepository(ApplicationDbContext db) => _db = db;

    public IQueryable<ArtworkSale> ArtworkSales => _db.ArtworkSales.AsNoTracking().Include(a => a.Artist);
}