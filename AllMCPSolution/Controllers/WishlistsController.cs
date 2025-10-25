using System.ComponentModel.DataAnnotations;
using System.Linq;
using AllMCPSolution.Models;
using System.ComponentModel;
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
[Route("wine-surfer")]
public sealed class WishlistsController : WineSurferControllerBase
{
    public sealed record WishlistSummaryViewModel(Guid Id, string Name, int WishCount);

    public sealed record WishlistFormModel(string Name);

    public sealed class WishlistsPageViewModel
    {
        public WishlistsPageViewModel(
            IReadOnlyList<WishlistSummaryViewModel> wishlists,
            WishlistFormModel createForm,
            WishlistFormModel editForm,
            Guid? editingWishlistId,
            IReadOnlyList<string> createErrors,
            IReadOnlyList<string> editErrors,
            string? statusMessage,
            string? errorMessage)
        {
            Wishlists = wishlists;
            CreateForm = createForm;
            EditForm = editForm;
            EditingWishlistId = editingWishlistId;
            CreateErrors = createErrors;
            EditErrors = editErrors;
            StatusMessage = statusMessage;
            ErrorMessage = errorMessage;
        }

        public IReadOnlyList<WishlistSummaryViewModel> Wishlists { get; }

        public WishlistFormModel CreateForm { get; }

        public WishlistFormModel EditForm { get; }

        public Guid? EditingWishlistId { get; }

        public IReadOnlyList<string> CreateErrors { get; }

        public IReadOnlyList<string> EditErrors { get; }

        public string? StatusMessage { get; }

        public string? ErrorMessage { get; }

        public bool HasWishlists => Wishlists.Count > 0;

        public bool HasCreateErrors => CreateErrors.Count > 0;

        public bool HasEditErrors => EditErrors.Count > 0;
    }

    public sealed class CreateWishlistRequest
    {
        [DefaultValue("")]
        public string? Name { get; set; }
    }

    public sealed class UpdateWishlistRequest
    {
        public Guid? Id { get; set; }

        [DefaultValue("")]
        public string? Name { get; set; }
    }

    public sealed class DeleteWishlistRequest
    {
        public Guid? Id { get; set; }
    }

    private const int WishlistNameMinLength = 2;
    private const int WishlistNameMaxLength = 256;
    private const string WishlistsStatusTempDataKey = "WineSurfer.Wishlists.Status";
    private const string WishlistsErrorTempDataKey = "WineSurfer.Wishlists.Error";

    private readonly IWishlistRepository _wishlistRepository;
    private readonly IWineSurferTopBarService _topBarService;

    public WishlistsController(
        IWishlistRepository wishlistRepository,
        IUserRepository userRepository,
        IWineSurferTopBarService topBarService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _wishlistRepository = wishlistRepository;
        _topBarService = topBarService;
    }

    [HttpGet("wishlists")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
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
        await PrepareWishlistsViewAsync(cancellationToken);

        var statusMessage = TempData.TryGetValue(WishlistsStatusTempDataKey, out var status)
            ? status as string
            : null;

        var errorMessage = TempData.TryGetValue(WishlistsErrorTempDataKey, out var error)
            ? error as string
            : null;

        var viewModel = await BuildViewModelAsync(
            currentUserId,
            cancellationToken,
            statusMessage: statusMessage,
            errorMessage: errorMessage);

        return View("Index", viewModel);
    }

    [HttpPost("wishlists")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateWishlistRequest request, CancellationToken cancellationToken)
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
        request ??= new CreateWishlistRequest();

        var validation = NormalizeAndValidateName(request.Name);

        var wishlists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);

        var duplicateExists = wishlists
            .Any(wishlist => string.Equals(wishlist.Name, validation.NormalizedName, StringComparison.OrdinalIgnoreCase));

        if (duplicateExists)
        {
            validation.Errors.Add("You already have a wishlist with that name.");
        }

        if (validation.Errors.Count > 0)
        {
            await PrepareWishlistsViewAsync(cancellationToken);

            var viewModel = await BuildViewModelAsync(
                currentUserId,
                cancellationToken,
                wishlists,
                createName: request.Name ?? string.Empty,
                createErrors: validation.Errors);

            return View("Index", viewModel);
        }

        var wishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            UserId = currentUserId
            UserId = currentUserId,
            Name = validation.NormalizedName
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
        TempData[WishlistsStatusTempDataKey] = $"Wishlist \"{wishlist.Name}\" created.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("wishlists/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromForm] UpdateWishlistRequest request, CancellationToken cancellationToken)
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

        if (request?.Id is null || request.Id == Guid.Empty)
        {
            TempData[WishlistsErrorTempDataKey] = "We couldn't find that wishlist.";
            return RedirectToAction(nameof(Index));
        }

        var wishlists = await _wishlistRepository.GetForUserAsync(currentUserId, cancellationToken);

        var wishlist = wishlists.FirstOrDefault(w => w.Id == request.Id.Value);
        if (wishlist is null)
        {
            TempData[WishlistsErrorTempDataKey] = "We couldn't find that wishlist.";
            return RedirectToAction(nameof(Index));
        }

        var validation = NormalizeAndValidateName(request.Name);

        var duplicateExists = wishlists
            .Any(existing => existing.Id != wishlist.Id && string.Equals(existing.Name, validation.NormalizedName, StringComparison.OrdinalIgnoreCase));

        if (duplicateExists)
        {
            validation.Errors.Add("You already have a wishlist with that name.");
        }

        if (validation.Errors.Count > 0)
        {
            await PrepareWishlistsViewAsync(cancellationToken);

            var viewModel = await BuildViewModelAsync(
                currentUserId,
                cancellationToken,
                wishlists,
                editName: request.Name ?? string.Empty,
                editingWishlistId: wishlist.Id,
                editErrors: validation.Errors);

            return View("Index", viewModel);
        }

        var wishlistToUpdate = new Wishlist
        {
            Id = wishlist.Id,
            UserId = wishlist.UserId,
            Name = validation.NormalizedName
        };

        await _wishlistRepository.UpdateAsync(wishlistToUpdate, cancellationToken);

        TempData[WishlistsStatusTempDataKey] = $"Wishlist \"{wishlistToUpdate.Name}\" updated.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("wishlists/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromForm] DeleteWishlistRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Challenge();
        }

        if (request?.Id is null || request.Id == Guid.Empty)
        {
            TempData[WishlistsErrorTempDataKey] = "We couldn't find that wishlist.";
            return RedirectToAction(nameof(Index));
        }

        var wishlist = await _wishlistRepository.GetByIdAsync(request.Id.Value, cancellationToken);

        if (wishlist is null || wishlist.UserId != currentUserId)
        {
            TempData[WishlistsErrorTempDataKey] = "We couldn't find that wishlist.";
            return RedirectToAction(nameof(Index));
        }

        await _wishlistRepository.DeleteAsync(wishlist.Id, cancellationToken);

        TempData[WishlistsStatusTempDataKey] = $"Wishlist \"{wishlist.Name}\" deleted.";

        return RedirectToAction(nameof(Index));
    }

    private async Task PrepareWishlistsViewAsync(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";
    }

    private async Task<WishlistsPageViewModel> BuildViewModelAsync(
        Guid userId,
        CancellationToken cancellationToken,
        IReadOnlyList<Wishlist>? wishlists = null,
        string? createName = null,
        string? editName = null,
        Guid? editingWishlistId = null,
        IReadOnlyList<string>? createErrors = null,
        IReadOnlyList<string>? editErrors = null,
        string? statusMessage = null,
        string? errorMessage = null)
    {
        var wishlistEntities = wishlists ?? await _wishlistRepository.GetForUserAsync(userId, cancellationToken);

        var summaries = wishlistEntities
            .Select(entity => new WishlistSummaryViewModel(
                entity.Id,
                entity.Name,
                entity.Wishes?.Count ?? 0))
            .ToList();

        return new WishlistsPageViewModel(
            summaries,
            new WishlistFormModel(createName ?? string.Empty),
            new WishlistFormModel(editName ?? string.Empty),
            editingWishlistId,
            createErrors ?? Array.Empty<string>(),
            editErrors ?? Array.Empty<string>(),
            statusMessage,
            errorMessage);
    }

    private static WishlistNameValidationResult NormalizeAndValidateName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add("Please enter a name for the wishlist.");
            return new WishlistNameValidationResult(string.Empty, errors);
        }

        if (normalized.Length < WishlistNameMinLength)
        {
            errors.Add($"Wishlist names must be at least {WishlistNameMinLength} characters long.");
        }

        if (normalized.Length > WishlistNameMaxLength)
        {
            errors.Add($"Wishlist names must be {WishlistNameMaxLength} characters or fewer.");
        }

        return new WishlistNameValidationResult(normalized, errors);
    }

    private readonly record struct WishlistNameValidationResult(string NormalizedName, List<string> Errors);

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
