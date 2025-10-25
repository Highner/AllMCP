using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AllMCPSolution.Controllers;

[Authorize]
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
            UserId = currentUserId,
            Name = validation.NormalizedName
        };

        await _wishlistRepository.AddAsync(wishlist, cancellationToken);

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
}
