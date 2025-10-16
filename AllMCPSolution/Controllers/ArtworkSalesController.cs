using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Data;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllMCPSolution.Controllers;

public class ArtworkSalesController : Controller
{
    private readonly IArtworkSaleRepository _repo;
    public ArtworkSalesController(IArtworkSaleRepository repo)
    {
        _repo = repo;
    }

    // View endpoint: /ArtworkSales
    [HttpGet("/ArtworkSales")]
    public IActionResult Index()
    {
        return View();
    }

    // API endpoints for CRUD

    // GET /api/artworksales?search=&sortBy=&sortDir=asc|desc
    [HttpGet("/api/artworksales")]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] string? sortBy, [FromQuery] string? sortDir, CancellationToken ct)
    {
        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var list = await _repo.GetAllAsync(search, sortBy, desc, 5000, ct);
        var result = list.Select(a => new
        {
            a.Id,
            a.Name,
            a.Height,
            a.Width,
            a.YearCreated,
            a.SaleDate,
            a.Technique,
            a.Category,
            a.Currency,
            a.LowEstimate,
            a.HighEstimate,
            a.HammerPrice,
            a.Sold,
            a.ArtistId,
            ArtistName = a.Artist != null ? ((a.Artist.FirstName ?? string.Empty) + " " + (a.Artist.LastName ?? string.Empty)).Trim() : string.Empty
        });
        return Ok(result);
    }

    // GET /api/artworksales/{id}
    [HttpGet("/api/artworksales/{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity == null) return NotFound();
        return Ok(entity);
    }

    // POST /api/artworksales
    [HttpPost("/api/artworksales")]
    public async Task<IActionResult> Create([FromBody] ArtworkSale input, CancellationToken ct)
    {
        if (input == null) return BadRequest();
        try
        {
            var created = await _repo.AddAsync(input, ct);
            return Created($"/api/artworksales/{created.Id}", created);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    // PUT /api/artworksales/{id}
    [HttpPut("/api/artworksales/{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] ArtworkSale input, CancellationToken ct)
    {
        try
        {
            var updated = await _repo.UpdateAsync(id, input, ct);
            if (updated == null) return NotFound();
            return Ok(updated);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    // DELETE /api/artworksales/{id}
    [HttpDelete("/api/artworksales/{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var ok = await _repo.DeleteAsync(id, ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    // GET /api/artworksales/categories
    [HttpGet("/api/artworksales/categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var cats = await _repo.GetCategoriesAsync(ct);
        return Ok(cats);
    }
}
