using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
public sealed class WishlistsController : WineSurferControllerBase
{
    public sealed class WishlistsViewModel
    {
        public Guid? SelectedWishlistId { get; set; }
        public string SelectedWishlistName { get; set; } = string.Empty;
        public IReadOnlyList<WishlistOption> Wishlists { get; set; } = Array.Empty<WishlistOption>();
        public IReadOnlyList<WishlistItemViewModel> Items { get; set; } = Array.Empty<WishlistItemViewModel>();
    }

    public sealed class WishlistOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int WishCount { get; set; }
    }

    public sealed class WishlistItemViewModel
    {
        public Guid WishId { get; set; }
        public Guid WineId { get; set; }
        public Guid WineVintageId { get; set; }
        public string WineName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Appellation { get; set; } = string.Empty;
        public string SubAppellation { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Variety { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string WineSearcherUrl { get; set; } = string.Empty;
        public int Vintage { get; set; }
        public bool IsInInventory { get; set; }
    }


    public sealed class CreateWishlistRequest
    {
        [Required]
        [StringLength(
            WishlistNameValidator.MaxLength,
            MinimumLength = WishlistNameValidator.MinLength)]
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

    public sealed record WishlistWishListItem(
        Guid WishId,
        Guid WineId,
        Guid WineVintageId,
        string Name,
        string Region,
        string Appellation,
        string SubAppellation,
        string Country,
        string Variety,
        string Color,
        string WineSearcherUrl,
        int Vintage,
        bool IsInInventory);

    private readonly IWishlistRepository _wishlistRepository;
    private readonly IWineVintageWishRepository _wishRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IWineSurferTopBarService _topBarService;
    private readonly IBottleLocationRepository _bottleLocationRepository;
    private readonly IBottleRepository _bottleRepository;

    public WishlistsController(
        IWishlistRepository wishlistRepository,
        IWineVintageWishRepository wishRepository,
        IWineVintageRepository wineVintageRepository,
        IWineRepository wineRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        IBottleLocationRepository bottleLocationRepository,
        IBottleRepository bottleRepository,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _wishlistRepository = wishlistRepository;
        _wishRepository = wishRepository;
        _wineVintageRepository = wineVintageRepository;
        _wineRepository = wineRepository;
        _topBarService = topBarService;
        _bottleLocationRepository = bottleLocationRepository;
        _bottleRepository = bottleRepository;
    }

    [HttpGet("/wine-manager/wishlists")]
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

    [HttpGet("/wine-manager/wishlists/{id:guid}")]
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

    [HttpPost("/wine-manager/wishlists")]
    public async Task<IActionResult> CreateWishlist(
        [FromBody] CreateWishlistRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        request ??= new CreateWishlistRequest();

        var validation = WishlistNameValidator.NormalizeAndValidate(
            request.Name,
            "Wishlist name is required.");

        var wishlists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);

        if (wishlists.Any(list =>
                string.Equals(list.Name, validation.NormalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            validation.Errors.Add("You already have a wishlist with that name.");
        }

        if (validation.Errors.Count > 0)
        {
            foreach (var error in validation.Errors)
            {
                ModelState.AddModelError(nameof(request.Name), error);
            }

            return ValidationProblem(ModelState);
        }

        var wishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            Name = validation.NormalizedName,
            UserId = currentUserId
        };

        await _wishlistRepository.AddAsync(wishlist, cancellationToken);

        var response = new WishlistSummaryResponse(wishlist.Id, wishlist.Name, 0);
        return CreatedAtAction(nameof(GetWishlist), new { id = wishlist.Id }, response);
    }

    [HttpGet("/wine-manager/wishlists/{wishlistId:guid}/wishes")]
    public async Task<IActionResult> GetWishes(Guid wishlistId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var wishlist = await _wishlistRepository.GetByIdAsync(wishlistId, cancellationToken);
        if (wishlist is null || wishlist.UserId != currentUserId)
        {
            return NotFound();
        }

        var wishes = await _wishRepository.GetForWishlistAsync(wishlist.Id, cancellationToken);
        var inventoryLookup = await BuildInventoryVintageLookupAsync(currentUserId, cancellationToken);

        var items = wishes.Select(w =>
        {
            var wine = w.WineVintage?.Wine;
            var wineId = wine?.Id ?? Guid.Empty;
            var vintage = w.WineVintage?.Vintage ?? 0;
            var subAppellation = wine?.SubAppellation?.Name ?? string.Empty;
            var appellation = wine?.SubAppellation?.Appellation?.Name ?? string.Empty;
            var region = wine?.SubAppellation?.Appellation?.Region?.Name ?? string.Empty;
            var country = wine?.SubAppellation?.Appellation?.Region?.Country?.Name ?? string.Empty;
            var variety = wine?.GrapeVariety ?? string.Empty;
            var color = wine?.Color.ToString() ?? string.Empty;
            var searchUrl = CreateWineSearcherUrl(w);
            var isInInventory = wineId != Guid.Empty && inventoryLookup.Contains((wineId, vintage));

            return new WishlistWishListItem(
                w.Id,
                wineId,
                w.WineVintage?.Id ?? Guid.Empty,
                wine?.Name ?? string.Empty,
                region,
                appellation,
                subAppellation,
                country,
                variety,
                color,
                searchUrl,
                vintage,
                isInInventory);
        });

        return Json(items);
    }

    [HttpPost("/wine-manager/wishlists/{wishlistId:guid}/wishes")]
    public async Task<IActionResult> AddWish(
        Guid wishlistId,
        [FromBody] AddWishlistItemRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (request is null)
        {
            ModelState.AddModelError(string.Empty, "A request body is required.");
            return ValidationProblem(ModelState);
        }

        if (!ModelState.IsValid)
        {
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

    [HttpPut("/wine-manager/wishlists/{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] CreateWishlistRequest? request, CancellationToken cancellationToken)
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

        request ??= new CreateWishlistRequest();
        var validation = WishlistNameValidator.NormalizeAndValidate(request.Name, "Wishlist name is required.");

        var userLists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);
        if (userLists.Any(list => list.Id != id && string.Equals(list.Name, validation.NormalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            validation.Errors.Add("You already have a wishlist with that name.");
        }

        if (validation.Errors.Count > 0)
        {
            foreach (var error in validation.Errors)
            {
                ModelState.AddModelError(nameof(request.Name), error);
            }

            return ValidationProblem(ModelState);
        }

        wishlist.Name = validation.NormalizedName;
        await _wishlistRepository.UpdateAsync(wishlist, cancellationToken);
        return Json(new WishlistSummaryResponse(wishlist.Id, wishlist.Name, wishlist.Wishes?.Count ?? 0));
    }

    [HttpDelete("/wine-manager/wishlists/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
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

        await _wishlistRepository.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("/wine-surfer/wishlists")]
    public async Task<IActionResult> Index([FromQuery] Guid? wishlistId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        await SetInventoryAddModalViewDataAsync(currentUserId, cancellationToken);

        var wishlists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);
        var wishlistOptions = wishlists
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new WishlistOption
            {
                Id = w.Id,
                Name = w.Name,
                WishCount = w.Wishes?.Count ?? 0
            })
            .ToList();

        Guid? selectedId = null;
        if (wishlistId.HasValue && wishlists.Any(w => w.Id == wishlistId.Value))
        {
            selectedId = wishlistId.Value;
        }
        else if (wishlists.Count > 0)
        {
            selectedId = wishlists[0].Id;
        }

        var inventoryLookup = await BuildInventoryVintageLookupAsync(currentUserId, cancellationToken);

        var items = new List<WishlistItemViewModel>();
        string selectedName = string.Empty;
        if (selectedId.HasValue)
        {
            var selectedWishlist = wishlists.First(w => w.Id == selectedId.Value);
            selectedName = selectedWishlist.Name;
            var wishes = await _wishRepository.GetForWishlistAsync(selectedId.Value, cancellationToken);
            items = wishes.Select(w => new WishlistItemViewModel
            {
                WishId = w.Id,
                WineId = w.WineVintage.Wine.Id,
                WineVintageId = w.WineVintage.Id,
                WineName = w.WineVintage.Wine.Name ?? string.Empty,
                Region = w.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Name ?? string.Empty,
                Appellation = w.WineVintage.Wine.SubAppellation?.Appellation?.Name ?? string.Empty,
                SubAppellation = w.WineVintage.Wine.SubAppellation?.Name ?? string.Empty,
                Country = w.WineVintage.Wine.SubAppellation?.Appellation?.Region?.Country?.Name ?? string.Empty,
                Variety = w.WineVintage.Wine.GrapeVariety ?? string.Empty,
                Color = w.WineVintage.Wine.Color.ToString(),
                WineSearcherUrl = CreateWineSearcherUrl(w),
                Vintage = w.WineVintage.Vintage,
                IsInInventory = w.WineVintage.Wine.Id != Guid.Empty &&
                    inventoryLookup.Contains((w.WineVintage.Wine.Id, w.WineVintage.Vintage))
            }).ToList();
        }

        var viewModel = new WishlistsViewModel
        {
            SelectedWishlistId = selectedId,
            SelectedWishlistName = selectedName,
            Wishlists = wishlistOptions,
            Items = items
        };

        Response.ContentType = "text/html; charset=utf-8";
        return View("Index", viewModel);
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

    private async Task SetInventoryAddModalViewDataAsync(Guid? currentUserId, CancellationToken cancellationToken)
    {
        InventoryAddModalViewModel viewModel;

        if (!currentUserId.HasValue)
        {
            viewModel = new InventoryAddModalViewModel();
        }
        else
        {
            var bottleLocations = await _bottleLocationRepository.GetAllAsync(cancellationToken);
            var userLocations = bottleLocations
                .Where(location => location.UserId == currentUserId.Value)
                .OrderBy(location => location.Name)
                .Select(location => new BottleLocationOption
                {
                    Id = location.Id,
                    Name = location.Name,
                    Capacity = location.Capacity
                })
                .ToList();

            viewModel = new InventoryAddModalViewModel
            {
                Locations = userLocations
            };
        }

        ViewData["InventoryAddModal"] = viewModel;
    }

    private async Task<HashSet<(Guid WineId, int Vintage)>> BuildInventoryVintageLookupAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<(Guid, int)>();

        if (userId == Guid.Empty)
        {
            return result;
        }

        var bottles = await _bottleRepository.GetForUserAsync(userId, cancellationToken);
        foreach (var bottle in bottles)
        {
            var wineId = bottle.WineVintage?.Wine?.Id ?? Guid.Empty;
            var vintage = bottle.WineVintage?.Vintage;

            if (wineId == Guid.Empty || !vintage.HasValue)
            {
                continue;
            }

            result.Add((wineId, vintage.Value));
        }

        return result;
    }

    private static string CreateWineSearcherUrl(WineVintageWish wish)
    {
        if (wish?.WineVintage?.Wine is null)
        {
            return "https://www.wine-searcher.com/";
        }

        var wine = wish.WineVintage.Wine;
        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(wine.Name))
        {
            segments.Add(wine.Name.Trim());
        }

        var subAppellation = wine.SubAppellation?.Name;
        if (!string.IsNullOrWhiteSpace(subAppellation))
        {
            segments.Add(subAppellation.Trim());
        }

        var appellation = wine.SubAppellation?.Appellation?.Name;
        if (!string.IsNullOrWhiteSpace(appellation))
        {
            var trimmed = appellation.Trim();
            if (!segments.Any(entry => string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                segments.Add(trimmed);
            }
        }

        var region = wine.SubAppellation?.Appellation?.Region?.Name;
        if (!string.IsNullOrWhiteSpace(region))
        {
            var trimmed = region.Trim();
            if (!segments.Any(entry => string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                segments.Add(trimmed);
            }
        }

        var country = wine.SubAppellation?.Appellation?.Region?.Country?.Name;
        if (!string.IsNullOrWhiteSpace(country))
        {
            var trimmed = country.Trim();
            if (!segments.Any(entry => string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                segments.Add(trimmed);
            }
        }

        var vintage = wish.WineVintage?.Vintage ?? 0;
        if (vintage >= 1900 && vintage <= 2100)
        {
            segments.Add(vintage.ToString(CultureInfo.InvariantCulture));
        }

        if (segments.Count == 0)
        {
            return "https://www.wine-searcher.com/";
        }

        var encoded = WebUtility.UrlEncode(string.Join(' ', segments));
        return string.IsNullOrWhiteSpace(encoded)
            ? "https://www.wine-searcher.com/"
            : $"https://www.wine-searcher.com/find/{encoded}";
    }
}
