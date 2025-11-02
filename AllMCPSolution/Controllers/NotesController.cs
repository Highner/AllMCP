using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public sealed class NotesController : WineSurferControllerBase
{
    public sealed class NotesViewModel
    {
        public IReadOnlyList<NoteEntryViewModel> Entries { get; init; } = Array.Empty<NoteEntryViewModel>();
        public int EntryCount => Entries.Count;
    }

    public sealed class NoteEntryViewModel
    {
        public Guid BottleId { get; init; }
        public Guid NoteId { get; init; }
        public Guid WineId { get; init; }
        public string WineName { get; init; } = string.Empty;
        public int? Vintage { get; init; }
        public string Appellation { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public DateTime? DrunkAt { get; init; }
        public string Note { get; init; } = string.Empty;
        public decimal? Score { get; init; }
        public bool NotTasted { get; init; }
    }

    private readonly IBottleRepository _bottleRepository;
    private readonly IWineSurferTopBarService _topBarService;

    public NotesController(
        IBottleRepository bottleRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _bottleRepository = bottleRepository;
        _topBarService = topBarService;
    }

    [HttpGet("notes")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var bottles = await _bottleRepository.GetForUserAsync(currentUserId.Value, cancellationToken);

        var entries = bottles
            .Where(bottle => bottle.IsDrunk)
            .Select(bottle => new
            {
                Bottle = bottle,
                Note = bottle.TastingNotes
                    .FirstOrDefault(note => note.UserId == currentUserId.Value)
            })
            .Where(pair => pair.Note is not null)
            .Select(pair =>
            {
                var note = pair.Note!;

                return new NoteEntryViewModel
            {
                BottleId = pair.Bottle.Id,
                NoteId = note.Id,
                WineId = pair.Bottle.WineVintage?.Wine?.Id ?? Guid.Empty,
                WineName = pair.Bottle.WineVintage?.Wine?.Name ?? string.Empty,
                Vintage = pair.Bottle.WineVintage?.Vintage,
                Appellation = pair.Bottle.WineVintage?.Wine?.SubAppellation?.Appellation?.Name ?? string.Empty,
                Region = pair.Bottle.WineVintage?.Wine?.SubAppellation?.Appellation?.Region?.Name ?? string.Empty,
                DrunkAt = pair.Bottle.DrunkAt,
                Note = (note.Note ?? string.Empty).Trim(),
                Score = note.Score,
                NotTasted = note.NotTasted
            };
            })
            .OrderByDescending(entry => entry.DrunkAt ?? DateTime.MinValue)
            .ThenBy(entry => entry.WineName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var viewModel = new NotesViewModel
        {
            Entries = entries
        };

        return View("Index", viewModel);
    }
}
