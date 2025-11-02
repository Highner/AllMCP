using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AllMCPSolution.Services;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-manager/wine-wizard")]
public sealed class WineWizardController : WineSurferControllerBase
{
    private readonly IBottleRepository _bottleRepository;
    private readonly IBottleShareRepository _bottleShareRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IBottleLocationRepository _bottleLocationRepository;

    private readonly IUserNotificationService _notifications;

    public WineWizardController(
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository,
        IBottleRepository bottleRepository,
        IBottleShareRepository bottleShareRepository,
        IWineRepository wineRepository,
        IWineVintageRepository wineVintageRepository,
        IBottleLocationRepository bottleLocationRepository,
        IUserNotificationService notifications)
        : base(userManager, userRepository)
    {
        _bottleRepository = bottleRepository;
        _bottleShareRepository = bottleShareRepository;
        _wineRepository = wineRepository;
        _wineVintageRepository = wineVintageRepository;
        _bottleLocationRepository = bottleLocationRepository;
        _notifications = notifications;
    }

    [HttpPost("share")]
    public async Task<IActionResult> Share([FromBody] WineWizardShareRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
        {
            return Challenge();
        }

        var existingBottleIds = await ValidateExistingBottleIdsAsync(
            request.ExistingBottleIds,
            currentUserId.Value,
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var recipientIds = await ValidateRecipientIdsAsync(
            request.RecipientUserIds,
            currentUserId.Value,
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (recipientIds.Count == 0)
        {
            ModelState.AddModelError(nameof(request.RecipientUserIds), "Select at least one fellow surfer to share with.");
            return ValidationProblem(ModelState);
        }

        var createdBottleIds = await CreateRequestedBottlesAsync(
            request.NewBottleRequests,
            currentUserId.Value,
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var allBottleIds = existingBottleIds
            .Concat(createdBottleIds)
            .Distinct()
            .ToList();

        if (allBottleIds.Count == 0)
        {
            ModelState.AddModelError(nameof(request.ExistingBottleIds), "Select or create at least one bottle to share.");
            return ValidationProblem(ModelState);
        }

        var shareResult = await CreateBottleSharesAsync(
            allBottleIds,
            recipientIds,
            currentUserId.Value,
            cancellationToken);

        var response = new WineWizardShareResponse
        {
            CreatedBottleIds = createdBottleIds,
            SharedBottleIds = shareResult.SharedBottleIds,
            RecipientUserIds = recipientIds,
            Message = shareResult.Message
        };

        return Json(response);
    }

    private async Task<List<Guid>> ValidateExistingBottleIdsAsync(
        IEnumerable<Guid>? bottleIds,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var valid = new List<Guid>();
        if (bottleIds is null)
        {
            return valid;
        }

        foreach (var bottleId in bottleIds)
        {
            if (bottleId == Guid.Empty)
            {
                continue;
            }

            var bottle = await _bottleRepository.GetByIdAsync(bottleId, cancellationToken);
            if (bottle is null || bottle.UserId != currentUserId)
            {
                ModelState.AddModelError(nameof(WineWizardShareRequest.ExistingBottleIds), "One of the selected bottles could not be found.");
                continue;
            }

            if (bottle.IsDrunk)
            {
                ModelState.AddModelError(nameof(WineWizardShareRequest.ExistingBottleIds), "You cannot share a bottle that has already been marked as drunk.");
                continue;
            }

            valid.Add(bottle.Id);
        }

        return valid;
    }

    private async Task<List<Guid>> ValidateRecipientIdsAsync(
        IEnumerable<Guid>? recipientIds,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var unique = new HashSet<Guid>();
        if (recipientIds is null)
        {
            return unique.ToList();
        }

        foreach (var candidate in recipientIds)
        {
            if (candidate == Guid.Empty || candidate == currentUserId)
            {
                continue;
            }

            if (!unique.Add(candidate))
            {
                continue;
            }

            var user = await _userRepository.GetByIdAsync(candidate, cancellationToken);
            if (user is null)
            {
                ModelState.AddModelError(nameof(WineWizardShareRequest.RecipientUserIds), "One of the selected fellow surfers could not be found.");
                unique.Remove(candidate);
            }
        }

        return unique.ToList();
    }

    private async Task<List<Guid>> CreateRequestedBottlesAsync(
        IReadOnlyList<WineWizardBottleRequest>? requests,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var created = new List<Guid>();
        if (requests is null || requests.Count == 0)
        {
            return created;
        }

        var pending = new List<PendingBottleCreation>();

        foreach (var request in requests)
        {
            if (request is null || request.WineId == Guid.Empty)
            {
                ModelState.AddModelError(nameof(WineWizardShareRequest.NewBottleRequests), "Wine must be selected before adding a bottle.");
                continue;
            }

            var wine = await _wineRepository.GetByIdAsync(request.WineId, cancellationToken);
            if (wine is null)
            {
                ModelState.AddModelError(nameof(WineWizardShareRequest.NewBottleRequests), "The selected wine could not be found.");
                continue;
            }

            BottleLocation? location = null;
            if (request.BottleLocationId.HasValue)
            {
                location = await _bottleLocationRepository.GetByIdAsync(request.BottleLocationId.Value, cancellationToken);
                if (location is null || location.UserId != currentUserId)
                {
                    ModelState.AddModelError(nameof(WineWizardShareRequest.NewBottleRequests), "Bottle location was not found.");
                    continue;
                }
            }

            var quantity = Math.Clamp(request.Quantity, 1, 12);
            pending.Add(new PendingBottleCreation(wine.Id, request.Vintage, quantity, location?.Id));
        }

        if (!ModelState.IsValid)
        {
            return created;
        }

        foreach (var pendingRequest in pending)
        {
            var wineVintage = await _wineVintageRepository.GetOrCreateAsync(
                pendingRequest.WineId,
                pendingRequest.Vintage,
                cancellationToken);

            for (var i = 0; i < pendingRequest.Quantity; i++)
            {
                var bottle = new Bottle
                {
                    Id = Guid.NewGuid(),
                    WineVintageId = wineVintage.Id,
                    Price = null,
                    IsDrunk = false,
                    DrunkAt = null,
                    BottleLocationId = pendingRequest.LocationId,
                    UserId = currentUserId
                };

                await _bottleRepository.AddAsync(bottle, cancellationToken);
                created.Add(bottle.Id);
            }
        }

        return created;
    }

    private async Task<BottleShareCreationResult> CreateBottleSharesAsync(
        IReadOnlyList<Guid> bottleIds,
        IReadOnlyList<Guid> recipientIds,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var sharedBottleIds = new HashSet<Guid>();

        foreach (var bottleId in bottleIds)
        {
            var existingShares = await _bottleShareRepository.GetSharesForBottleAsync(bottleId, cancellationToken);
            var existingRecipients = existingShares
                .Select(share => share.SharedWithUserId)
                .ToHashSet();

            foreach (var recipientId in recipientIds)
            {
                if (existingRecipients.Contains(recipientId))
                {
                    continue;
                }

                var share = new BottleShare
                {
                    Id = Guid.NewGuid(),
                    BottleId = bottleId,
                    SharedByUserId = currentUserId,
                    SharedWithUserId = recipientId,
                    SharedAt = DateTime.UtcNow
                };

                await _bottleShareRepository.AddAsync(share, cancellationToken);
                existingRecipients.Add(recipientId);
                sharedBottleIds.Add(bottleId);

                // Notify recipient of new bottle share
                await _notifications.NotifyBottleShareCreatedAsync(recipientId, bottleId, currentUserId);
            }
        }

        var message = BuildSuccessMessage(sharedBottleIds.Count, recipientIds.Count);
        return new BottleShareCreationResult(sharedBottleIds.ToList(), message);
    }

    private static string BuildSuccessMessage(int bottleCount, int recipientCount)
    {
        if (bottleCount <= 0 || recipientCount <= 0)
        {
            return "Wine sharing completed.";
        }

        var bottleWord = bottleCount == 1 ? "bottle" : "bottles";
        var recipientWord = recipientCount == 1 ? "fellow surfer" : "fellow surfers";
        return $"Shared {bottleCount} {bottleWord} with {recipientCount} {recipientWord}.";
    }

    private sealed record PendingBottleCreation(Guid WineId, int Vintage, int Quantity, Guid? LocationId);

    private sealed record BottleShareCreationResult(IReadOnlyList<Guid> SharedBottleIds, string Message);
}

public class WineWizardShareRequest
{
    public IReadOnlyList<Guid>? ExistingBottleIds { get; set; }

    public IReadOnlyList<WineWizardBottleRequest>? NewBottleRequests { get; set; }

    public IReadOnlyList<Guid>? RecipientUserIds { get; set; }
}

public class WineWizardBottleRequest
{
    [Required]
    public Guid WineId { get; set; }

    [Range(1900, 2100)]
    public int Vintage { get; set; }

    [Range(1, 12)]
    public int Quantity { get; set; } = 1;

    public Guid? BottleLocationId { get; set; }
}

public class WineWizardShareResponse
{
    public IReadOnlyList<Guid> CreatedBottleIds { get; set; } = Array.Empty<Guid>();

    public IReadOnlyList<Guid> SharedBottleIds { get; set; } = Array.Empty<Guid>();

    public IReadOnlyList<Guid> RecipientUserIds { get; set; } = Array.Empty<Guid>();

    public string Message { get; set; } = string.Empty;
}
