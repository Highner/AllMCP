using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BottleLocationsController : ControllerBase
{
    private readonly IBottleLocationRepository _locations;

    public BottleLocationsController(IBottleLocationRepository locations)
    {
        _locations = locations;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var locations = await _locations.GetAllAsync(ct);
        var projected = locations
            .Select(l => new { l.Id, l.Name })
            .ToList();

        return Ok(projected);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var location = await _locations.GetByIdAsync(id, ct);
        if (location is null)
        {
            return NotFound();
        }

        return Ok(new { location.Id, location.Name });
    }

    public sealed class CreateBottleLocationRequest
    {
        public string? Name { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBottleLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Location name is required." });
        }

        var trimmedName = request.Name.Trim();
        var existing = await _locations.FindByNameAsync(trimmedName, ct);
        if (existing is not null)
        {
            return Conflict(new
            {
                message = $"Location '{trimmedName}' already exists.",
                locationId = existing.Id
            });
        }

        var location = new BottleLocation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName
        };

        await _locations.AddAsync(location, ct);

        return CreatedAtAction(nameof(GetById), new { id = location.Id }, new { location.Id, location.Name });
    }

    public sealed class UpdateBottleLocationRequest
    {
        public string? Name { get; set; }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBottleLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Location name is required." });
        }

        var existing = await _locations.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound();
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _locations.FindByNameAsync(trimmedName, ct);
        if (duplicate is not null && duplicate.Id != id)
        {
            return Conflict(new
            {
                message = $"Location '{trimmedName}' already exists.",
                locationId = duplicate.Id
            });
        }

        existing.Name = trimmedName;
        await _locations.UpdateAsync(existing, ct);

        return Ok(new { existing.Id, existing.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _locations.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound();
        }

        await _locations.DeleteAsync(id, ct);
        return NoContent();
    }
}
