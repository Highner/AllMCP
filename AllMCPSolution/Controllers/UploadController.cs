using Microsoft.AspNetCore.Mvc;
using AllMCPSolution.Repositories;
using AllMCPSolution.Models;
using System.Linq;
using System.Collections.Concurrent;

namespace AllMCPSolution.Artworks;

[ApiController]
[Route("api")] // Keep exact path compatibility: /api/upload
public class UploadController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, List<ArtworkSale>> PendingBatches = new();

    private readonly IArtworkSaleRepository _repo;

    public UploadController(IArtworkSaleRepository repo)
    {
        _repo = repo;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromServices] IHttpContextAccessor accessor, CancellationToken ct)
    {
        var context = accessor.HttpContext!;
        try
        {
            var form = await context.Request.ReadFormAsync(ct);
            var files = form.Files.GetFiles("files");

            var artistIdString = form["artistId"].ToString();
            if (string.IsNullOrEmpty(artistIdString) || !Guid.TryParse(artistIdString, out var artistId))
                return BadRequest(new { message = "Valid Artist ID is required" });

            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files uploaded" });

            var allSales = new List<ArtworkSale>();
            var fileResults = new List<object>();

            foreach (var file in files)
            {
                try
                {
                    using var stream = file.OpenReadStream();
                    var sales = ArtworkSaleParser.ParseFromStream(stream);
                    foreach (var sale in sales)
                        sale.ArtistId = artistId;

                    allSales.AddRange(sales);

                    fileResults.Add(new { fileName = file.FileName, parsed = sales.Count, success = true });
                }
                catch (Exception ex)
                {
                    fileResults.Add(new { fileName = file.FileName, parsed = 0, success = false, error = ex.Message });
                }
            }

            // Store parsed sales in a temporary in-memory batch for later saving
            var batchId = Guid.NewGuid();
            PendingBatches[batchId] = allSales;

            return Ok(new
            {
                message = "Files parsed successfully. Not saved yet.",
                filesProcessed = files.Count,
                fileDetails = fileResults,
                totalParsed = allSales.Count,
                inserted = 0,
                skipped = 0,
                batchId,
                preview = allSales.Select(s => new
                {
                    s.Name,
                    s.YearCreated,
                    s.SaleDate,
                    s.Technique,
                    s.Category,
                    s.Currency,
                    s.LowEstimate,
                    s.HighEstimate,
                    s.HammerPrice,
                    s.Sold,
                    s.Width,
                    s.Height
                })
            });
        }
        catch (Exception ex)
        {
            return Problem($"Error uploading files: {ex.Message} {ex.StackTrace}");
        }
    }

    public class SaveUploadRequest
    {
        public Guid BatchId { get; set; }
    }

    [HttpPost("upload/save")]
    public async Task<IActionResult> Save([FromBody] SaveUploadRequest request, CancellationToken ct)
    {
        if (request == null || request.BatchId == Guid.Empty)
            return BadRequest(new { message = "batchId is required" });

        if (!PendingBatches.TryRemove(request.BatchId, out var sales) || sales == null || sales.Count == 0)
            return NotFound(new { message = "No pending batch found for the provided batchId" });

        try
        {
            var inserted = await _repo.AddRangeIfNotExistsAsync(sales, ct);
            return Ok(new
            {
                message = "Sales saved successfully",
                inserted,
                skipped = sales.Count - inserted,
                total = sales.Count
            });
        }
        catch (Exception ex)
        {
            // In case of failure, put the batch back to allow retry
            PendingBatches[request.BatchId] = sales;
            return Problem($"Error saving sales: {ex.Message} {ex.StackTrace}");
        }
    }
}
