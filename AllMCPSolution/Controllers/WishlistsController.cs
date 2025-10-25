using System.ComponentModel.DataAnnotations;
using System.Linq;
using AllMCPSolution.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-manager/wishlists")]
public class WishlistsController : Controller
{
    private readonly IWishlistRepository _wishlistRepository;
    private readonly IWineVintageWishRepository _wishRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IWineRepository _wineRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public WishlistsController(
        IWishlistRepository wishlistRepository,
        IWineVintageWishRepository wishRepository,
        IWineVintageRepository wineVintageRepository,
        IWineRepository wineRepository,
        UserManager<ApplicationUser> userManager)
    {
        _wishlistRepository = wishlistRepository;
        _wishRepository = wishRepository;
        _wineVintageRepository = wineVintageRepository;
        _wineRepository = wineRepository;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetWishlists(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var wishlists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);
        var response = wishlists
            .Select(list => new WishlistSummaryResponse(
                list.Id,
                list.Name,
                list.Wishes?.Count ?? 0))
            .OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Json(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWishlist(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var wishlist = await _wishlistRepository.GetByIdAsync(id, cancellationToken);
        if (wishlist is null || wishlist.UserId != currentUserId)
        {
            return NotFound();
        }

        var response = new WishlistSummaryResponse(
            wishlist.Id,
            wishlist.Name,
            wishlist.Wishes?.Count ?? 0);

        return Json(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateWishlist(
        [FromBody] CreateWishlistRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(request.Name), "Wishlist name is required.");
            return ValidationProblem(ModelState);
        }

        var existing = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);
        var hasDuplicate = existing.Any(list =>
            string.Equals(list.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (hasDuplicate)
        {
            ModelState.AddModelError(nameof(request.Name), "You already have a wishlist with that name.");
            return ValidationProblem(ModelState);
        }

        var wishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            UserId = currentUserId
        };

        await _wishlistRepository.AddAsync(wishlist, cancellationToken);

        var response = new WishlistSummaryResponse(wishlist.Id, wishlist.Name, 0);
        return CreatedAtAction(nameof(GetWishlist), new { id = wishlist.Id }, response);
    }

    [HttpPost("{wishlistId:guid}/wishes")]
    public async Task<IActionResult> AddWish(
        Guid wishlistId,
        [FromBody] AddWishlistItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (request.WineId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.WineId), "Wine must be selected.");
            return ValidationProblem(ModelState);
        }

        if (request.Vintage < 1900 || request.Vintage > 2100)
        {
            ModelState.AddModelError(nameof(request.Vintage), "Enter a vintage between 1900 and 2100.");
            return ValidationProblem(ModelState);
        }

        var wishlist = await _wishlistRepository.GetByIdAsync(wishlistId, cancellationToken);
        if (wishlist is null || wishlist.UserId != currentUserId)
        {
            return NotFound();
        }

        var wine = await _wineRepository.GetByIdAsync(request.WineId, cancellationToken);
        if (wine is null)
        {
            ModelState.AddModelError(nameof(request.WineId), "The selected wine could not be found.");
            return ValidationProblem(ModelState);
        }

        var wineVintage = await _wineVintageRepository.GetOrCreateAsync(wine.Id, request.Vintage, cancellationToken);
        var existingWish = await _wishRepository.FindAsync(wishlist.Id, wineVintage.Id, cancellationToken);
        if (existingWish is not null)
        {
            return Conflict(new
            {
                message = "That wine is already saved to this wishlist.",
                wishId = existingWish.Id
            });
        }

        var wish = new WineVintageWish
        {
            Id = Guid.NewGuid(),
            WishlistId = wishlist.Id,
            WineVintageId = wineVintage.Id
        };

        await _wishRepository.AddAsync(wish, cancellationToken);

        var response = new WishlistWishResponse(
            wish.Id,
            wishlist.Id,
            wishlist.Name,
            wineVintage.Id,
            wine.Id,
            wineVintage.Vintage,
            wine.Name ?? string.Empty);

        return Json(response);
    }

    private Guid? GetCurrentUserId()
    {
        var idValue = _userManager.GetUserId(User);
        return Guid.TryParse(idValue, out var parsedId) ? parsedId : null;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId.HasValue)
        {
            userId = currentUserId.Value;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    public sealed class CreateWishlistRequest
    {
        [Required]
        [StringLength(80, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class AddWishlistItemRequest
    {
        [Required]
        public Guid WineId { get; set; }

        [Range(1900, 2100)]
        public int Vintage { get; set; }
    }

    public sealed record WishlistSummaryResponse(Guid Id, string Name, int WishCount);

    public sealed record WishlistWishResponse(
        Guid Id,
        Guid WishlistId,
        string WishlistName,
        Guid WineVintageId,
        Guid WineId,
        int Vintage,
        string WineName);
}
