using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AllMCPSolution.Repositories;
using AllMCPSolution.Models;

namespace AllMCPSolution.Artists;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ArtistsController : ControllerBase
{
    private readonly IArtistRepository _artists;

    public ArtistsController(IArtistRepository artists)
    {
        _artists = artists;
    }

    // GET /api/artists
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var artists = await _artists.GetAllAsync(ct);
        var projected = artists.Select(a => new { a.Id, a.FirstName, a.LastName }).ToList();
        return Ok(projected);
    }

    public class CreateArtistRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    // POST /api/artists
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateArtistRequest request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                return BadRequest(new { message = "First name and last name are required" });

            var existing = await _artists.FindByNameAsync(request.FirstName, request.LastName, ct);
            if (existing != null)
            {
                return Conflict(new
                {
                    message = $"Artist '{request.FirstName} {request.LastName}' already exists",
                    artistId = existing.Id
                });
            }

            var artist = new Artist
            {
                Id = Guid.NewGuid(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim()
            };

            await _artists.AddAsync(artist, ct);

            return Ok(new
            {
                message = $"Artist '{artist.FirstName} {artist.LastName}' added successfully",
                artistId = artist.Id,
                firstName = artist.FirstName,
                lastName = artist.LastName
            });
        }
        catch (Exception ex)
        {
            return Problem($"Error adding artist: {ex.Message}");
        }
    }
}
