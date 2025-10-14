using Microsoft.AspNetCore.Mvc;
using AllMCPSolution.Repositories;
using AllMCPSolution.Models;

namespace AllMCPSolution.Artworks;

[ApiController]
[Route("api")] // Keep exact path compatibility: /api/upload
public class UploadController : ControllerBase
{
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

            var inserted = await _repo.AddRangeIfNotExistsAsync(allSales, ct);

            return Ok(new
            {
                message = "Files processed and saved",
                filesProcessed = files.Count,
                fileDetails = fileResults,
                totalParsed = allSales.Count,
                inserted,
                skipped = allSales.Count - inserted
            });
        }
        catch (Exception ex)
        {
            return Problem($"Error uploading files: {ex.Message} {ex.StackTrace}");
        }
    }
}
