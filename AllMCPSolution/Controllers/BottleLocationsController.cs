using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BottleLocationsController : ControllerBase
{
    private readonly IBottleLocationRepository _locations;
    private readonly IUserRepository _users;

    public BottleLocationsController(IBottleLocationRepository locations, IUserRepository users)
    {
        _locations = locations;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var locations = await _locations.GetAllAsync(ct);
        var projected = locations
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.UserId,
                UserName = l.User?.Name
            })
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

        return Ok(new
        {
            location.Id,
            location.Name,
            location.UserId,
            UserName = location.User?.Name
        });
    }

    public sealed class CreateBottleLocationRequest
    {
        public string? Name { get; set; }
        public Guid? UserId { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBottleLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Location name is required." });
        }

        if (!request.UserId.HasValue || request.UserId.Value == Guid.Empty)
        {
            return BadRequest(new { message = "A valid user identifier is required." });
        }

        var trimmedName = request.Name.Trim();
        var user = await _users.GetByIdAsync(request.UserId.Value, ct);
        if (user is null)
        {
            return BadRequest(new { message = "User was not found." });
        }

        var existing = await _locations.FindByNameAsync(trimmedName, request.UserId.Value, ct);
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
            Name = trimmedName,
            UserId = user.Id
        };

        await _locations.AddAsync(location, ct);

        return CreatedAtAction(nameof(GetById), new { id = location.Id }, new
        {
            location.Id,
            location.Name,
            location.UserId,
            UserName = user.Name
        });
    }

    public sealed class UpdateBottleLocationRequest
    {
        public string? Name { get; set; }
        public Guid? UserId { get; set; }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBottleLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Location name is required." });
        }

        if (!request.UserId.HasValue || request.UserId.Value == Guid.Empty)
        {
            return BadRequest(new { message = "A valid user identifier is required." });
        }

        var existing = await _locations.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound();
        }

        var user = await _users.GetByIdAsync(request.UserId.Value, ct);
        if (user is null)
        {
            return BadRequest(new { message = "User was not found." });
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _locations.FindByNameAsync(trimmedName, request.UserId.Value, ct);
        if (duplicate is not null && duplicate.Id != id)
        {
            return Conflict(new
            {
                message = $"Location '{trimmedName}' already exists.",
                locationId = duplicate.Id
            });
        }

        existing.Name = trimmedName;
        existing.UserId = user.Id;
        await _locations.UpdateAsync(existing, ct);

        return Ok(new
        {
            existing.Id,
            existing.Name,
            existing.UserId,
            UserName = user.Name
        });
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
