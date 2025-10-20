using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-manager")]
public class WineInventoryController : Controller
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITastingNoteRepository _tastingNoteRepository;

    public WineInventoryController(
        IBottleRepository bottleRepository,
        IBottleLocationRepository bottleLocationRepository,
        IWineRepository wineRepository,
        IWineVintageRepository wineVintageRepository,
        ISubAppellationRepository subAppellationRepository,
        IUserRepository userRepository,
        ITastingNoteRepository tastingNoteRepository)
    {
        _bottleRepository = bottleRepository;
        _bottleLocationRepository = bottleLocationRepository;
        _wineRepository = wineRepository;
        _wineVintageRepository = wineVintageRepository;
        _subAppellationRepository = subAppellationRepository;
        _userRepository = userRepository;
        _tastingNoteRepository = tastingNoteRepository;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? status,
        [FromQuery] string? color,
        [FromQuery] string? search,
        [FromQuery] string? sortField,
        [FromQuery] string? sortDir,
        CancellationToken cancellationToken)
    {
        var bottles = await _bottleRepository.GetAllAsync(cancellationToken);

        var bottleGroups = bottles
            .GroupBy(b => b.WineVintageId)
            .ToList();

        var averageScores = bottleGroups
            .Select(group => new
            {
                group.Key,
                Scores = group
                    .Where(bottle => bottle.IsDrunk)
                    .SelectMany(bottle => bottle.TastingNotes)
                    .Select(note => note.Score)
                    .Where(score => score.HasValue && score.Value > 0)
                    .Select(score => score.Value)
                    .ToList()
            })
            .Where(entry => entry.Scores.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => decimal.Round(entry.Scores.Average(), 1, MidpointRounding.AwayFromZero));

        decimal? GetAverageScore(Guid wineVintageId) =>
            averageScores.TryGetValue(wineVintageId, out var avg)
                ? avg
                : null;

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        WineColor? filterColor = null;
        if (!string.IsNullOrWhiteSpace(color) && Enum.TryParse<WineColor>(color, true, out var parsedColor))
        {
            filterColor = parsedColor;
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedSortField = string.IsNullOrWhiteSpace(sortField) ? "wine" : sortField.Trim().ToLowerInvariant();
        var normalizedSortDir = string.IsNullOrWhiteSpace(sortDir) ? "asc" : sortDir.Trim().ToLowerInvariant();

        IEnumerable<Bottle> query = bottles;

        query = normalizedStatus switch
        {
            "drunk" => query.Where(b => b.IsDrunk),
            "cellared" or "undrunk" => query.Where(b => !b.IsDrunk),
            _ => query
        };

        if (filterColor.HasValue)
        {
            query = query.Where(b => b.WineVintage.Wine.Color == filterColor.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(b =>
                (!string.IsNullOrEmpty(b.WineVintage.Wine.Name) && b.WineVintage.Wine.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                (b.WineVintage.Wine.SubAppellation?.Name?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (b.WineVintage.Wine.SubAppellation?.Appellation?.Name?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                b.WineVintage.Vintage.ToString(CultureInfo.InvariantCulture).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        var descending = string.Equals(normalizedSortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedEnumerable<Bottle> ordered = normalizedSortField switch
        {
            "appellation" => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenByDescending(b => b.WineVintage.Wine.SubAppellation?.Name)
                : query.OrderBy(b => b.WineVintage.Wine.SubAppellation?.Appellation?.Name)
                    .ThenBy(b => b.WineVintage.Wine.SubAppellation?.Name),
            "vintage" => descending
                ? query.OrderByDescending(b => b.WineVintage.Vintage)
                : query.OrderBy(b => b.WineVintage.Vintage),
            "color" => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.Color)
                : query.OrderBy(b => b.WineVintage.Wine.Color),
            "status" => descending
                ? query.OrderByDescending(b => b.IsDrunk)
                : query.OrderBy(b => b.IsDrunk),
            "score" => descending
                ? query.OrderByDescending(b => GetAverageScore(b.WineVintageId))
                : query.OrderBy(b => GetAverageScore(b.WineVintageId)),
            _ => descending
                ? query.OrderByDescending(b => b.WineVintage.Wine.Name)
                : query.OrderBy(b => b.WineVintage.Wine.Name)
        };

        var orderedWithTies = ordered
            .ThenBy(b => b.WineVintage.Wine.Name)
            .ThenBy(b => b.WineVintage.Vintage)
            .ThenBy(b => b.Id);

        var groupedBottles = orderedWithTies
            .GroupBy(b => b.WineVintageId)
            .Select(group =>
            {
                var bottlesInGroup = group.ToList();
                return CreateBottleGroupViewModel(group.Key, bottlesInGroup, GetAverageScore(group.Key));
            })
            .ToList();

        IOrderedEnumerable<WineInventoryBottleViewModel> orderedGroups = normalizedSortField switch
        {
            "appellation" => descending
                ? groupedBottles.OrderByDescending(b => b.Appellation).ThenByDescending(b => b.SubAppellation)
                : groupedBottles.OrderBy(b => b.Appellation).ThenBy(b => b.SubAppellation),
            "vintage" => descending
                ? groupedBottles.OrderByDescending(b => b.Vintage)
                : groupedBottles.OrderBy(b => b.Vintage),
            "color" => descending
                ? groupedBottles.OrderByDescending(b => b.Color)
                : groupedBottles.OrderBy(b => b.Color),
            "status" => descending
                ? groupedBottles.OrderByDescending(b => b.StatusLabel)
                : groupedBottles.OrderBy(b => b.StatusLabel),
            "score" => descending
                ? groupedBottles.OrderByDescending(b => b.AverageScore)
                : groupedBottles.OrderBy(b => b.AverageScore),
            _ => descending
                ? groupedBottles.OrderByDescending(b => b.WineName)
                : groupedBottles.OrderBy(b => b.WineName)
        };

        var items = orderedGroups
            .ThenBy(b => b.WineName)
            .ThenBy(b => b.Vintage)
            .ToList();

        var viewModel = new WineInventoryViewModel
        {
            Status = normalizedStatus,
            Color = filterColor?.ToString(),
            Search = normalizedSearch,
            SortField = normalizedSortField,
            SortDirection = descending ? "desc" : "asc",
            Bottles = items,
            StatusOptions = new List<FilterOption>
            {
                new("all", "All Bottles"),
                new("cellared", "Cellared"),
                new("drunk", "Drunk")
            },
            ColorOptions = new List<FilterOption>
            {
                new(string.Empty, "All Colors"),
                new(WineColor.Red.ToString(), "Red"),
                new(WineColor.White.ToString(), "White"),
                new(WineColor.Rose.ToString(), "Rosé")
            }
        };

        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", viewModel);
    }

    [HttpGet("bottles/{wineVintageId:guid}")]
    public async Task<IActionResult> GetBottleGroupDetails(Guid wineVintageId, CancellationToken cancellationToken)
    {
        var response = await BuildBottleGroupResponseAsync(wineVintageId, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Json(response);
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetReferenceData(CancellationToken cancellationToken)
    {
        var subAppellations = await _subAppellationRepository.GetAllAsync(cancellationToken);
        var bottleLocations = await _bottleLocationRepository.GetAllAsync(cancellationToken);
        var users = await _userRepository.GetAllAsync(cancellationToken);

        var response = new InventoryReferenceDataResponse
        {
            SubAppellations = subAppellations
                .Select(sa => new SubAppellationOption
                {
                    Id = sa.Id,
                    Label = BuildSubAppellationLabel(sa)
                })
                .ToList(),
            BottleLocations = bottleLocations
                .Select(bl => new BottleLocationOption
                {
                    Id = bl.Id,
                    Name = bl.Name
                })
                .ToList(),
            Users = users
                .Select(u => new UserOption
                {
                    Id = u.Id,
                    Name = u.Name
                })
                .ToList()
        };

        return Json(response);
    }

    [HttpGet("bottles/{bottleId:guid}/notes")]
    public async Task<IActionResult> GetBottleNotes(Guid bottleId, CancellationToken cancellationToken)
    {
        var response = await BuildBottleNotesResponseAsync(bottleId, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Json(response);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] WineGroupCreateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedName = request.WineName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.WineName), "Wine name is required.");
            return ValidationProblem(ModelState);
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            ModelState.AddModelError(nameof(request.SubAppellationId), "Sub-appellation was not found.");
            return ValidationProblem(ModelState);
        }

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null)
        {
            ModelState.AddModelError(nameof(request.WineName), "A wine with the same name already exists for the selected sub-appellation.");
            return ValidationProblem(ModelState);
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Color = request.Color,
            SubAppellationId = subAppellation.Id
        };

        await _wineRepository.AddAsync(wine, cancellationToken);

        var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, request.Vintage, cancellationToken);

        for (var i = 0; i < request.InitialBottleCount; i++)
        {
            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                IsDrunk = false,
                DrunkAt = null,
                Price = null,
                BottleLocationId = bottleLocation?.Id
            };

            await _bottleRepository.AddAsync(bottle, cancellationToken);
        }

        var response = await BuildBottleGroupResponseAsync(wineVintage.Id, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load wine group after creation.");
        }

        return Json(response);
    }

    [HttpPut("groups/{wineVintageId:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid wineVintageId, [FromBody] WineGroupUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var existingVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId, cancellationToken);
        if (existingVintage is null)
        {
            return NotFound();
        }

        var trimmedName = request.WineName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.WineName), "Wine name is required.");
            return ValidationProblem(ModelState);
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            ModelState.AddModelError(nameof(request.SubAppellationId), "Sub-appellation was not found.");
            return ValidationProblem(ModelState);
        }

        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null && duplicate.Id != existingVintage.WineId)
        {
            ModelState.AddModelError(nameof(request.WineName), "A wine with the same name already exists for the selected sub-appellation.");
            return ValidationProblem(ModelState);
        }

        var wine = await _wineRepository.GetByIdAsync(existingVintage.WineId, cancellationToken);
        if (wine is null)
        {
            return NotFound();
        }

        wine.Name = trimmedName;
        wine.Color = request.Color;
        wine.SubAppellationId = subAppellation.Id;

        await _wineRepository.UpdateAsync(wine, cancellationToken);

        var updatedVintage = new WineVintage
        {
            Id = existingVintage.Id,
            WineId = wine.Id,
            Vintage = request.Vintage
        };

        await _wineVintageRepository.UpdateAsync(updatedVintage, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(updatedVintage.Id, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load wine group after update.");
        }

        return Json(response);
    }

    [HttpDelete("groups/{wineVintageId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid wineVintageId, CancellationToken cancellationToken)
    {
        var existingVintage = await _wineVintageRepository.GetByIdAsync(wineVintageId, cancellationToken);
        if (existingVintage is null)
        {
            return NotFound();
        }

        var bottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);
        foreach (var bottle in bottles)
        {
            await _bottleRepository.DeleteAsync(bottle.Id, cancellationToken);
        }

        await _wineVintageRepository.DeleteAsync(wineVintageId, cancellationToken);

        var wine = await _wineRepository.GetByIdAsync(existingVintage.WineId, cancellationToken);
        if (wine is not null && (wine.WineVintages == null || wine.WineVintages.Count == 0))
        {
            await _wineRepository.DeleteAsync(wine.Id, cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("bottles")]
    public async Task<IActionResult> CreateBottle([FromBody] BottleMutationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        User? user = null;
        if (request.UserId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(request.UserId.Value, cancellationToken);
            if (user is null)
            {
                ModelState.AddModelError(nameof(request.UserId), "User was not found.");
                return ValidationProblem(ModelState);
            }
        }

        var bottle = new Bottle
        {
            Id = Guid.NewGuid(),
            WineVintageId = request.WineVintageId,
            Price = request.Price,
            IsDrunk = request.IsDrunk,
            DrunkAt = NormalizeDrunkAt(request.IsDrunk, request.DrunkAt),
            BottleLocationId = bottleLocation?.Id,
            UserId = user?.Id
        };

        await _bottleRepository.AddAsync(bottle, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(request.WineVintageId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load bottle group after creation.");
        }

        return Json(response);
    }

    [HttpPut("bottles/{id:guid}")]
    public async Task<IActionResult> UpdateBottle(Guid id, [FromBody] BottleMutationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null)
        {
            return NotFound();
        }

        BottleLocation? bottleLocation = null;
        if (request.BottleLocationId.HasValue)
        {
            bottleLocation = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
            if (bottleLocation is null)
            {
                ModelState.AddModelError(nameof(request.BottleLocationId), "Bottle location was not found.");
                return ValidationProblem(ModelState);
            }
        }

        if (request.UserId.HasValue)
        {
            var user = await _userRepository.GetByIdAsync(request.UserId.Value, cancellationToken);
            if (user is null)
            {
                ModelState.AddModelError(nameof(request.UserId), "User was not found.");
                return ValidationProblem(ModelState);
            }

            bottle.UserId = user.Id;
        }
        else
        {
            bottle.UserId = null;
        }

        bottle.Price = request.Price;
        bottle.IsDrunk = request.IsDrunk;
        bottle.DrunkAt = NormalizeDrunkAt(request.IsDrunk, request.DrunkAt);
        bottle.BottleLocationId = bottleLocation?.Id;

        await _bottleRepository.UpdateAsync(bottle, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, cancellationToken);
        return Json(response ?? new BottleGroupDetailsResponse
        {
            Group = null,
            Details = Array.Empty<WineInventoryBottleDetailViewModel>()
        });
    }

    [HttpPost("notes")]
    public async Task<IActionResult> CreateNote([FromBody] TastingNoteCreateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedNote = request.Note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            ModelState.AddModelError(nameof(request.Note), "Note text is required.");
            return ValidationProblem(ModelState);
        }

        var bottle = await _bottleRepository.GetByIdAsync(request.BottleId, cancellationToken);
        if (bottle is null)
        {
            ModelState.AddModelError(nameof(request.BottleId), "Bottle was not found.");
            return ValidationProblem(ModelState);
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError(nameof(request.UserId), "User was not found.");
            return ValidationProblem(ModelState);
        }

        var entity = new TastingNote
        {
            Id = Guid.NewGuid(),
            BottleId = bottle.Id,
            Note = trimmedNote!,
            Score = request.Score,
            UserId = user.Id
        };

        await _tastingNoteRepository.AddAsync(entity, cancellationToken);

        var response = await BuildBottleNotesResponseAsync(bottle.Id, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load tasting notes after creation.");
        }

        return Json(response);
    }

    [HttpPut("notes/{noteId:guid}")]
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] TastingNoteUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedNote = request.Note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            ModelState.AddModelError(nameof(request.Note), "Note text is required.");
            return ValidationProblem(ModelState);
        }

        var existing = await _tastingNoteRepository.GetByIdAsync(noteId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError(nameof(request.UserId), "User was not found.");
            return ValidationProblem(ModelState);
        }

        existing.Note = trimmedNote!;
        existing.Score = request.Score;
        existing.UserId = user.Id;

        await _tastingNoteRepository.UpdateAsync(existing, cancellationToken);

        var response = await BuildBottleNotesResponseAsync(existing.BottleId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load tasting notes after update.");
        }

        return Json(response);
    }

    [HttpDelete("notes/{noteId:guid}")]
    public async Task<IActionResult> DeleteNote(Guid noteId, CancellationToken cancellationToken)
    {
        var existing = await _tastingNoteRepository.GetByIdAsync(noteId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        await _tastingNoteRepository.DeleteAsync(noteId, cancellationToken);

        var response = await BuildBottleNotesResponseAsync(existing.BottleId, cancellationToken);
        if (response is null)
        {
            return Json(new BottleNotesResponse
            {
                Bottle = null,
                Notes = Array.Empty<TastingNoteViewModel>()
            });
        }

        return Json(response);
    }

    [HttpDelete("bottles/{id:guid}")]
    public async Task<IActionResult> DeleteBottle(Guid id, CancellationToken cancellationToken)
    {
        var bottle = await _bottleRepository.GetByIdAsync(id, cancellationToken);
        if (bottle is null)
        {
            return NotFound();
        }

        await _bottleRepository.DeleteAsync(id, cancellationToken);

        var response = await BuildBottleGroupResponseAsync(bottle.WineVintageId, cancellationToken);
        return Json(response ?? new BottleGroupDetailsResponse
        {
            Group = null,
            Details = Array.Empty<WineInventoryBottleDetailViewModel>()
        });
    }

    private async Task<BottleGroupDetailsResponse?> BuildBottleGroupResponseAsync(Guid wineVintageId, CancellationToken cancellationToken)
    {
        var bottles = await _bottleRepository.GetByWineVintageIdAsync(wineVintageId, cancellationToken);

        if (!bottles.Any())
        {
            return new BottleGroupDetailsResponse
            {
                Group = null,
                Details = Array.Empty<WineInventoryBottleDetailViewModel>()
            };
        }

        var averageScore = CalculateAverageScore(bottles);
        var summary = CreateBottleGroupViewModel(wineVintageId, bottles, averageScore);
        var details = bottles
            .OrderBy(b => b.IsDrunk)
            .ThenBy(b => b.DrunkAt ?? DateTime.MaxValue)
            .Select(b => new WineInventoryBottleDetailViewModel
            {
                BottleId = b.Id,
                Price = b.Price,
                IsDrunk = b.IsDrunk,
                DrunkAt = b.DrunkAt,
                BottleLocationId = b.BottleLocationId,
                BottleLocation = b.BottleLocation?.Name ?? "—",
                UserId = b.UserId,
                UserName = b.User?.Name ?? string.Empty,
                Vintage = b.WineVintage.Vintage,
                WineName = b.WineVintage.Wine.Name,
                AverageScore = CalculateAverageScore(b)
            })
            .ToList();

        return new BottleGroupDetailsResponse
        {
            Group = summary,
            Details = details
        };
    }

    private async Task<BottleNotesResponse?> BuildBottleNotesResponseAsync(Guid bottleId, CancellationToken cancellationToken)
    {
        var bottle = await _bottleRepository.GetByIdAsync(bottleId, cancellationToken);
        if (bottle is null)
        {
            return null;
        }

        var notes = await _tastingNoteRepository.GetByBottleIdAsync(bottleId, cancellationToken);

        var noteViewModels = notes
            .Select(note => new TastingNoteViewModel
            {
                Id = note.Id,
                Note = note.Note,
                Score = note.Score,
                UserId = note.UserId,
                UserName = note.User?.Name ?? string.Empty
            })
            .ToList();

        var bottleSummary = new BottleNotesBottleSummary
        {
            BottleId = bottle.Id,
            WineVintageId = bottle.WineVintageId,
            WineName = bottle.WineVintage.Wine.Name,
            Vintage = bottle.WineVintage.Vintage,
            BottleLocation = bottle.BottleLocation?.Name,
            UserId = bottle.UserId,
            UserName = bottle.User?.Name ?? string.Empty,
            IsDrunk = bottle.IsDrunk,
            DrunkAt = bottle.DrunkAt
        };

        return new BottleNotesResponse
        {
            Bottle = bottleSummary,
            Notes = noteViewModels
        };
    }

    private static WineInventoryBottleViewModel CreateBottleGroupViewModel(Guid groupId, IReadOnlyCollection<Bottle> bottles, decimal? averageScore)
    {
        var firstBottle = bottles.First();
        var totalCount = bottles.Count;
        var drunkCount = bottles.Count(b => b.IsDrunk);

        var (statusLabel, statusClass) = drunkCount switch
        {
            var d when d == 0 => ("Cellared", "cellared"),
            var d when d == totalCount => ("Drunk", "drunk"),
            _ => ("Mixed", "mixed")
        };

        return new WineInventoryBottleViewModel
        {
            WineVintageId = groupId,
            WineId = firstBottle.WineVintage.Wine.Id,
            WineName = firstBottle.WineVintage.Wine.Name,
            SubAppellation = firstBottle.WineVintage.Wine.SubAppellation?.Name,
            Appellation = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Name,
            SubAppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Id,
            AppellationId = firstBottle.WineVintage.Wine.SubAppellation?.Appellation?.Id,
            Vintage = firstBottle.WineVintage.Vintage,
            Color = firstBottle.WineVintage.Wine.Color.ToString(),
            BottleCount = totalCount,
            StatusLabel = statusLabel,
            StatusCssClass = statusClass,
            AverageScore = averageScore
        };
    }

    private static string BuildSubAppellationLabel(SubAppellation subAppellation)
    {
        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(subAppellation.Name))
        {
            segments.Add(subAppellation.Name);
        }

        if (!string.IsNullOrWhiteSpace(subAppellation.Appellation?.Name))
        {
            segments.Add(subAppellation.Appellation!.Name);
        }

        if (!string.IsNullOrWhiteSpace(subAppellation.Appellation?.Region?.Name))
        {
            segments.Add(subAppellation.Appellation!.Region!.Name);
        }

        return segments.Count > 0 ? string.Join(" • ", segments) : "Unspecified";
    }

    private static decimal? CalculateAverageScore(IEnumerable<Bottle> bottles)
    {
        var scores = bottles
            .Where(bottle => bottle.IsDrunk)
            .SelectMany(bottle => bottle.TastingNotes)
            .Select(note => note.Score)
            .Where(score => score.HasValue && score.Value > 0)
            .Select(score => score!.Value)
            .ToList();

        if (scores.Count == 0)
        {
            return null;
        }

        return decimal.Round((decimal)scores.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static decimal? CalculateAverageScore(Bottle bottle)
    {
        var scores = bottle.TastingNotes
            .Select(note => note.Score)
            .Where(score => score.HasValue && score.Value > 0)
            .Select(score => score!.Value)
            .ToList();

        if (scores.Count == 0)
        {
            return null;
        }

        return decimal.Round((decimal)scores.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static DateTime? NormalizeDrunkAt(bool isDrunk, DateTime? drunkAt)
    {
        if (!isDrunk)
        {
            return null;
        }

        if (!drunkAt.HasValue)
        {
            return DateTime.UtcNow;
        }

        var value = drunkAt.Value;
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value;
    }
}

public class WineInventoryViewModel
{
    public string Status { get; set; } = "all";
    public string? Color { get; set; }
    public string? Search { get; set; }
    public string SortField { get; set; } = "wine";
    public string SortDirection { get; set; } = "asc";
    public IReadOnlyList<WineInventoryBottleViewModel> Bottles { get; set; } = Array.Empty<WineInventoryBottleViewModel>();
    public IReadOnlyList<FilterOption> StatusOptions { get; set; } = Array.Empty<FilterOption>();
    public IReadOnlyList<FilterOption> ColorOptions { get; set; } = Array.Empty<FilterOption>();
}

public class WineInventoryBottleViewModel
{
    public Guid WineVintageId { get; set; }
    public Guid WineId { get; set; }
    public string WineName { get; set; } = string.Empty;
    public string? SubAppellation { get; set; }
    public string? Appellation { get; set; }
    public Guid? SubAppellationId { get; set; }
    public Guid? AppellationId { get; set; }
    public int Vintage { get; set; }
    public string Color { get; set; } = string.Empty;
    public int BottleCount { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusCssClass { get; set; } = string.Empty;
    public decimal? AverageScore { get; set; }
}

public class WineInventoryBottleDetailViewModel
{
    public Guid BottleId { get; set; }
    public decimal? Price { get; set; }
    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }
    public Guid? BottleLocationId { get; set; }
    public string BottleLocation { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Vintage { get; set; }
    public string WineName { get; set; } = string.Empty;
    public decimal? AverageScore { get; set; }
}

public class BottleGroupDetailsResponse
{
    public WineInventoryBottleViewModel? Group { get; set; }
    public IReadOnlyList<WineInventoryBottleDetailViewModel> Details { get; set; } = Array.Empty<WineInventoryBottleDetailViewModel>();
}

public class BottleMutationRequest
{
    [Required]
    public Guid WineVintageId { get; set; }

    public decimal? Price { get; set; }

    public bool IsDrunk { get; set; }

    public DateTime? DrunkAt { get; set; }

    public Guid? BottleLocationId { get; set; }

    public Guid? UserId { get; set; }
}

public record FilterOption(string Value, string Label);

public class WineGroupUpdateRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string WineName { get; set; } = string.Empty;

    [Range(1900, 2100)]
    public int Vintage { get; set; }

    [Required]
    public WineColor Color { get; set; }

    [Required]
    public Guid SubAppellationId { get; set; }
}

public class WineGroupCreateRequest : WineGroupUpdateRequest
{
    [Range(1, 5000)]
    public int InitialBottleCount { get; set; } = 1;

    public Guid? BottleLocationId { get; set; }
}

public class InventoryReferenceDataResponse
{
    public IReadOnlyList<SubAppellationOption> SubAppellations { get; set; } = Array.Empty<SubAppellationOption>();
    public IReadOnlyList<BottleLocationOption> BottleLocations { get; set; } = Array.Empty<BottleLocationOption>();
    public IReadOnlyList<UserOption> Users { get; set; } = Array.Empty<UserOption>();
}

public class BottleNotesResponse
{
    public BottleNotesBottleSummary? Bottle { get; set; }
    public IReadOnlyList<TastingNoteViewModel> Notes { get; set; } = Array.Empty<TastingNoteViewModel>();
}

public class BottleNotesBottleSummary
{
    public Guid BottleId { get; set; }
    public Guid WineVintageId { get; set; }
    public string WineName { get; set; } = string.Empty;
    public int Vintage { get; set; }
    public string? BottleLocation { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsDrunk { get; set; }
    public DateTime? DrunkAt { get; set; }
}

public class TastingNoteViewModel
{
    public Guid Id { get; set; }
    public string Note { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
}

public class TastingNoteUpdateRequest
{
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string Note { get; set; } = string.Empty;

    [Range(0, 10)]
    public decimal? Score { get; set; }

    [Required]
    public Guid UserId { get; set; }
}

public class TastingNoteCreateRequest : TastingNoteUpdateRequest
{
    [Required]
    public Guid BottleId { get; set; }
}

public class SubAppellationOption
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class BottleLocationOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserOption
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
