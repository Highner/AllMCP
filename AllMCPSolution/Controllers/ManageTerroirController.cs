using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AllMCPSolution.Controllers;

[Authorize]
[Route("wine-surfer")]
public class ManageTerroirController : WineSurferControllerBase
{
    #region  request models

      private readonly record struct MergeRequestValidationResult(bool IsValid, Guid LeaderId, List<Guid> FollowerIds, string? ErrorMessage)
    {
        public static MergeRequestValidationResult Invalid(string message) => new(false, Guid.Empty, new List<Guid>(), message);

        public static MergeRequestValidationResult Valid(Guid leaderId, List<Guid> followerIds) => new(true, leaderId, followerIds, null);
    }

    private readonly record struct MergeEntityLabels(string Singular, string Plural);

    public sealed class CreateCountryRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }
    }

    public sealed class UpdateCountryRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }
    }

    public sealed class CreateRegionRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid CountryId { get; set; }
    }

    public sealed class UpdateRegionRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid CountryId { get; set; }
    }

    public sealed class CreateAppellationRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid RegionId { get; set; }
    }

    public sealed class UpdateAppellationRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [Required]
        public Guid RegionId { get; set; }
    }

    public sealed class CreateSubAppellationRequest
    {
        [StringLength(256)]
        public string? Name { get; set; }

        [Required]
        public Guid AppellationId { get; set; }
    }

    public sealed class UpdateSubAppellationRequest
    {
        [StringLength(256)]
        public string? Name { get; set; }

        [Required]
        public Guid AppellationId { get; set; }
    }

    public sealed class CreateWineRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(256)]
        public string? GrapeVariety { get; set; }

        [Required]
        public Guid SubAppellationId { get; set; }

        [Required]
        public WineColor Color { get; set; }
    }

    public sealed class UpdateWineRequest
    {
        [Required]
        [StringLength(256, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(256)]
        public string? GrapeVariety { get; set; }

        [Required]
        public Guid SubAppellationId { get; set; }

        [Required]
        public WineColor Color { get; set; }
    }

    public sealed class MergeTerroirRequest
    {
        [Required]
        public Guid LeaderId { get; set; }

        public List<Guid> EntityIds { get; set; } = new();
    }

    #endregion
   
    
    private const string TerroirHighlightSectionKey = "HighlightSection";
    private const string TerroirHighlightIdKey = "HighlightId";
    private static readonly MergeEntityLabels CountryMergeLabels = new("country", "countries");
    private static readonly MergeEntityLabels RegionMergeLabels = new("region", "regions");
    private static readonly MergeEntityLabels AppellationMergeLabels = new("appellation", "appellations");
    private static readonly MergeEntityLabels SubAppellationMergeLabels = new("sub-appellation", "sub-appellations");
    private static readonly MergeEntityLabels WineMergeLabels = new("wine", "wines");
    
      

    private readonly IWineRepository _wineRepository;

    private readonly ISisterhoodInvitationRepository _sisterhoodInvitationRepository;

    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;

    private readonly ITerroirMergeRepository _terroirMergeRepository;

    private static readonly TimeSpan SentInvitationNotificationWindow = TimeSpan.FromDays(7);


    private readonly IWineSurferTopBarService _topBarService;

    public ManageTerroirController(
        IWineRepository wineRepository,
        IUserRepository userRepository,
        ISisterhoodInvitationRepository sisterhoodInvitationRepository,
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        ITerroirMergeRepository terroirMergeRepository,
        IWineSurferTopBarService topBarService,
        UserManager<ApplicationUser> userManager) : base(userManager, userRepository)
    {
        _wineRepository = wineRepository;
        _sisterhoodInvitationRepository = sisterhoodInvitationRepository;
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _terroirMergeRepository = terroirMergeRepository;
        _topBarService = topBarService;
    }
    
    [Authorize]
    [HttpGet("manage-terroir")]
    public async Task<IActionResult> ManageTerroir(CancellationToken cancellationToken)
    {
        var currentPath = HttpContext?.Request?.Path.Value ?? string.Empty;
        ViewData["WineSurferTopBarModel"] = await _topBarService.BuildAsync(User, currentPath, cancellationToken);
        Response.ContentType = "text/html; charset=utf-8";

        var now = DateTime.UtcNow;
        WineSurferCurrentUser? currentUser = null;
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations = Array.Empty<WineSurferIncomingSisterhoodInvitation>();
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications = Array.Empty<WineSurferSentInvitationNotification>();
        Guid? currentUserId = null;
        string? normalizedEmail = null;
        ApplicationUser? domainUser = null;
        var isAdmin = false;
        string domainUserSummary = string.Empty;
        string domainUserProfile = string.Empty;

        if (User?.Identity?.IsAuthenticated == true)
        {
            var identityName = User.Identity?.Name;
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            normalizedEmail = StringUtilities.NormalizeEmailCandidate(email);
            currentUserId = GetCurrentUserId();

            if (currentUserId.HasValue)
            {
                domainUser = await _userRepository.GetByIdAsync(currentUserId.Value, cancellationToken);
            }

            if (domainUser is null && !string.IsNullOrWhiteSpace(identityName))
            {
                domainUser = await _userRepository.FindByNameAsync(identityName, cancellationToken);
            }

            var displayName = StringUtilities.ResolveDisplayName(domainUser?.Name, identityName, email);

            if (domainUser is not null)
            {
                currentUserId = domainUser.Id;
                normalizedEmail ??= StringUtilities.NormalizeEmailCandidate(domainUser.Email);
                isAdmin = domainUser.IsAdmin;
            }

            if (string.IsNullOrWhiteSpace(normalizedEmail) && StringUtilities.LooksLikeEmail(displayName))
            {
                normalizedEmail = StringUtilities.NormalizeEmailCandidate(displayName);
            }

            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(email) || domainUser is not null)
            {
                currentUser = new WineSurferCurrentUser(
                    domainUser?.Id,
                    displayName ?? email ?? string.Empty,
                    email,
                    domainUserSummary,
                    domainUserProfile,
                    domainUser?.SuggestionBudget,
                    isAdmin,
                    ProfilePhotoUtilities.CreateDataUrl(domainUser?.ProfilePhoto, domainUser?.ProfilePhotoContentType));
            }

            if (currentUserId.HasValue || normalizedEmail is not null)
            {
                incomingInvitations = (await _sisterhoodInvitationRepository.GetForInviteeAsync(currentUserId, normalizedEmail, cancellationToken))
                    .Where(invitation => invitation.Status == SisterhoodInvitationStatus.Pending)
                    .Select(invitation =>
                    {
                        var matchesUserId = currentUserId.HasValue && invitation.InviteeUserId == currentUserId.Value;
                        var matchesEmail = normalizedEmail is not null && string.Equals(invitation.InviteeEmail, normalizedEmail, StringComparison.Ordinal);

                        return new
                        {
                            Invitation = invitation,
                            MatchesUserId = matchesUserId,
                            MatchesEmail = matchesEmail,
                        };
                    })
                    .Where(entry => entry.MatchesUserId || entry.MatchesEmail)
                    .Select(entry => new WineSurferIncomingSisterhoodInvitation(
                        entry.Invitation.Id,
                        entry.Invitation.SisterhoodId,
                        entry.Invitation.Sisterhood?.Name ?? "Sisterhood",
                        entry.Invitation.Sisterhood?.Description,
                        entry.Invitation.InviteeEmail,
                        entry.Invitation.Status,
                        entry.Invitation.CreatedAt,
                        entry.Invitation.UpdatedAt,
                        entry.Invitation.InviteeUserId,
                        entry.MatchesUserId,
                        entry.MatchesEmail))
                    .ToList();
            }
        }

        if (!isAdmin)
        {
            return Forbid();
        }

        if (currentUserId.HasValue)
        {
            var acceptedInvitations = await _sisterhoodInvitationRepository.GetAcceptedForAdminAsync(
                currentUserId.Value,
                now - SentInvitationNotificationWindow,
                cancellationToken);

            sentInvitationNotifications = NotificationService.CreateSentInvitationNotifications(acceptedInvitations);
        }

        var statusMessage = TempData.ContainsKey("StatusMessage")
            ? TempData["StatusMessage"] as string
            : null;
        var errorMessage = TempData.ContainsKey("ErrorMessage")
            ? TempData["ErrorMessage"] as string
            : null;

        var highlightSection = TempData.ContainsKey(TerroirHighlightSectionKey)
            ? TempData[TerroirHighlightSectionKey] as string
            : null;
        var highlightId = TempData.ContainsKey(TerroirHighlightIdKey)
            ? TempData[TerroirHighlightIdKey] as string
            : null;

        var viewModel = await BuildTerroirManagementViewModel(
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            statusMessage,
            errorMessage,
            highlightSection,
            highlightId,
            cancellationToken);

        return View("Index", viewModel);
    }

    private void SetTerroirHighlight(string section, Guid id)
    {
        TempData[TerroirHighlightSectionKey] = section;
        TempData[TerroirHighlightIdKey] = id.ToString();
    }

    private MergeRequestValidationResult ValidateMergeRequest(MergeTerroirRequest? request, MergeEntityLabels labels)
    {
        if (request is null)
        {
            return MergeRequestValidationResult.Invalid($"Select at least two {labels.Plural} to merge.");
        }

        if (request.LeaderId == Guid.Empty)
        {
            return MergeRequestValidationResult.Invalid($"Select a leading {labels.Singular}.");
        }

        var ids = request.EntityIds is null
            ? new List<Guid>()
            : request.EntityIds.Where(id => id != Guid.Empty).ToList();

        if (!ids.Contains(request.LeaderId))
        {
            ids.Add(request.LeaderId);
        }

        var distinctIds = ids.Distinct().ToList();
        var followers = distinctIds.Where(id => id != request.LeaderId).ToList();

        if (followers.Count == 0)
        {
            return MergeRequestValidationResult.Invalid($"Select at least two {labels.Plural} to merge.");
        }

        return MergeRequestValidationResult.Valid(request.LeaderId, followers);
    }

    private static string CreateMergeStatusMessage(MergeEntityLabels labels, string leaderName, int followersMerged)
    {
        if (followersMerged <= 0)
        {
            return $"Selected {labels.Plural} are already consolidated.";
        }

        var noun = followersMerged == 1 ? labels.Singular : labels.Plural;
        return $"Merged {followersMerged} {noun} into {leaderName}.";
    }

    [Authorize]
    [HttpPost("countries")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCountry([FromForm] CreateCountryRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Country name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _countryRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Country '{trimmedName}' already exists.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var country = new Country
        {
            Id = Guid.NewGuid(),
            Name = trimmedName
        };

        await _countryRepository.AddAsync(country, cancellationToken);

        SetTerroirHighlight("country", country.Id);

        TempData["StatusMessage"] = $"Country '{country.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("countries/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCountry(Guid id, [FromForm] UpdateCountryRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _countryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Country name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _countryRepository.FindByNameAsync(trimmedName, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Country '{trimmedName}' already exists.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;

        await _countryRepository.UpdateAsync(existing, cancellationToken);

        SetTerroirHighlight("country", existing.Id);

        TempData["StatusMessage"] = $"Country '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("countries/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCountry(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _countryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _regionRepository.AnyForCountryAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove regions for '{existing.Name}' before deleting the country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _countryRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Country '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("countries/merge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeCountries([FromForm] MergeTerroirRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var validation = ValidateMergeRequest(request, CountryMergeLabels);
        if (!validation.IsValid)
        {
            TempData["ErrorMessage"] = validation.ErrorMessage;
            return RedirectToAction(nameof(ManageTerroir));
        }

        try
        {
            var result = await _terroirMergeRepository.MergeCountriesAsync(validation.LeaderId, validation.FollowerIds, cancellationToken);
            SetTerroirHighlight("country", result.LeaderId);
            TempData["StatusMessage"] = CreateMergeStatusMessage(CountryMergeLabels, result.LeaderName, result.FollowersMerged);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected countries. Please try again.";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected countries. Please try again.";
        }

        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRegion([FromForm] CreateRegionRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Region name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.CountryId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var country = await _countryRepository.GetByIdAsync(request.CountryId, cancellationToken);
        if (country is null)
        {
            TempData["ErrorMessage"] = "Selected country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var duplicate = await _regionRepository.FindByNameAndCountryAsync(trimmedName, country.Id, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Region '{trimmedName}' already exists for {country.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            CountryId = country.Id
        };

        await _regionRepository.AddAsync(region, cancellationToken);

        SetTerroirHighlight("region", region.Id);

        TempData["StatusMessage"] = $"Region '{region.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRegion(Guid id, [FromForm] UpdateRegionRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _regionRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Region name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.CountryId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a country.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var country = await _countryRepository.GetByIdAsync(request.CountryId, cancellationToken);
        if (country is null)
        {
            TempData["ErrorMessage"] = "Selected country could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var duplicate = await _regionRepository.FindByNameAndCountryAsync(trimmedName, country.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Region '{trimmedName}' already exists for {country.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.CountryId = country.Id;

        await _regionRepository.UpdateAsync(existing, cancellationToken);

        SetTerroirHighlight("region", existing.Id);

        TempData["StatusMessage"] = $"Region '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRegion(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _regionRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _appellationRepository.AnyForRegionAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove appellations for '{existing.Name}' before deleting the region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _regionRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Region '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("regions/merge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeRegions([FromForm] MergeTerroirRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var validation = ValidateMergeRequest(request, RegionMergeLabels);
        if (!validation.IsValid)
        {
            TempData["ErrorMessage"] = validation.ErrorMessage;
            return RedirectToAction(nameof(ManageTerroir));
        }

        try
        {
            var result = await _terroirMergeRepository.MergeRegionsAsync(validation.LeaderId, validation.FollowerIds, cancellationToken);
            SetTerroirHighlight("region", result.LeaderId);
            TempData["StatusMessage"] = CreateMergeStatusMessage(RegionMergeLabels, result.LeaderName, result.FollowersMerged);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected regions. Please try again.";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected regions. Please try again.";
        }

        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAppellation([FromForm] CreateAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Appellation name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.RegionId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = await _regionRepository.GetByIdAsync(request.RegionId, cancellationToken);
        if (region is null)
        {
            TempData["ErrorMessage"] = "Selected region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _appellationRepository.FindByNameAndRegionAsync(trimmedName, region.Id, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Appellation '{trimmedName}' already exists in {region.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = new Appellation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            RegionId = region.Id
        };

        await _appellationRepository.AddAsync(appellation, cancellationToken);

        SetTerroirHighlight("appellation", appellation.Id);

        TempData["StatusMessage"] = $"Appellation '{appellation.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAppellation(Guid id, [FromForm] UpdateAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _appellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Appellation name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.RegionId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a region.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var region = await _regionRepository.GetByIdAsync(request.RegionId, cancellationToken);
        if (region is null)
        {
            TempData["ErrorMessage"] = "Selected region could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _appellationRepository.FindByNameAndRegionAsync(trimmedName, region.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Appellation '{trimmedName}' already exists in {region.Name}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.RegionId = region.Id;

        await _appellationRepository.UpdateAsync(existing, cancellationToken);

        SetTerroirHighlight("appellation", existing.Id);

        TempData["StatusMessage"] = $"Appellation '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAppellation(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _appellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _subAppellationRepository.AnyForAppellationAsync(id, cancellationToken))
        {
            TempData["ErrorMessage"] = $"Remove sub-appellations for '{existing.Name}' before deleting the appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _appellationRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Appellation '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("appellations/merge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeAppellations([FromForm] MergeTerroirRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var validation = ValidateMergeRequest(request, AppellationMergeLabels);
        if (!validation.IsValid)
        {
            TempData["ErrorMessage"] = validation.ErrorMessage;
            return RedirectToAction(nameof(ManageTerroir));
        }

        try
        {
            var result = await _terroirMergeRepository.MergeAppellationsAsync(validation.LeaderId, validation.FollowerIds, cancellationToken);
            SetTerroirHighlight("appellation", result.LeaderId);
            TempData["StatusMessage"] = CreateMergeStatusMessage(AppellationMergeLabels, result.LeaderName, result.FollowersMerged);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected appellations. Please try again.";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected appellations. Please try again.";
        }

        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubAppellation([FromForm] CreateSubAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (request.AppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select an appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = await _appellationRepository.GetByIdAsync(request.AppellationId, cancellationToken);
        if (appellation is null)
        {
            TempData["ErrorMessage"] = "Selected appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellationName = string.IsNullOrWhiteSpace(appellation.Name) ? "the selected appellation" : appellation.Name.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var lookupName = trimmedName ?? string.Empty;
        var duplicate = await _subAppellationRepository.FindByNameAndAppellationAsync(lookupName, appellation.Id, cancellationToken);
        if (duplicate is not null)
        {
            var duplicateLabel = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
            TempData["ErrorMessage"] = $"Sub-appellation '{duplicateLabel}' already exists in {appellationName}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = new SubAppellation
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            AppellationId = appellation.Id
        };

        await _subAppellationRepository.AddAsync(subAppellation, cancellationToken);

        SetTerroirHighlight("sub-appellation", subAppellation.Id);

        var displayName = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
        TempData["StatusMessage"] = $"Sub-appellation '{displayName}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSubAppellation(Guid id, [FromForm] UpdateSubAppellationRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _subAppellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.AppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select an appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellation = await _appellationRepository.GetByIdAsync(request.AppellationId, cancellationToken);
        if (appellation is null)
        {
            TempData["ErrorMessage"] = "Selected appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var appellationName = string.IsNullOrWhiteSpace(appellation.Name) ? "the selected appellation" : appellation.Name.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var lookupName = trimmedName ?? string.Empty;
        var duplicate = await _subAppellationRepository.FindByNameAndAppellationAsync(lookupName, appellation.Id, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            var duplicateLabel = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
            TempData["ErrorMessage"] = $"Sub-appellation '{duplicateLabel}' already exists in {appellationName}.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        existing.Name = trimmedName;
        existing.AppellationId = appellation.Id;
        existing.Appellation = null;

        await _subAppellationRepository.UpdateAsync(existing, cancellationToken);

        SetTerroirHighlight("sub-appellation", existing.Id);

        var displayName = string.IsNullOrWhiteSpace(trimmedName) ? "Unknown sub-appellation" : trimmedName;
        TempData["StatusMessage"] = $"Sub-appellation '{displayName}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubAppellation(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _subAppellationRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (await _wineRepository.AnyBySubAppellationAsync(id, cancellationToken))
        {
            var displayName = string.IsNullOrWhiteSpace(existing.Name) ? "Unknown sub-appellation" : existing.Name.Trim();
            TempData["ErrorMessage"] = $"Remove wines for '{displayName}' before deleting the sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _subAppellationRepository.DeleteAsync(id, cancellationToken);

        var deletedName = string.IsNullOrWhiteSpace(existing.Name) ? "Unknown sub-appellation" : existing.Name.Trim();
        TempData["StatusMessage"] = $"Sub-appellation '{deletedName}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("sub-appellations/merge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeSubAppellations([FromForm] MergeTerroirRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var validation = ValidateMergeRequest(request, SubAppellationMergeLabels);
        if (!validation.IsValid)
        {
            TempData["ErrorMessage"] = validation.ErrorMessage;
            return RedirectToAction(nameof(ManageTerroir));
        }

        try
        {
            var result = await _terroirMergeRepository.MergeSubAppellationsAsync(validation.LeaderId, validation.FollowerIds, cancellationToken);
            SetTerroirHighlight("sub-appellation", result.LeaderId);
            TempData["StatusMessage"] = CreateMergeStatusMessage(SubAppellationMergeLabels, result.LeaderName, result.FollowersMerged);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected sub-appellations. Please try again.";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected sub-appellations. Please try again.";
        }

        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWine([FromForm] CreateWineRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Wine name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.SubAppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            TempData["ErrorMessage"] = "Selected sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null)
        {
            TempData["ErrorMessage"] = $"Wine '{trimmedName}' already exists for the selected sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var grapeVariety = string.IsNullOrWhiteSpace(request.GrapeVariety) ? string.Empty : request.GrapeVariety.Trim();

        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            GrapeVariety = grapeVariety,
            Color = request.Color,
            SubAppellationId = subAppellation.Id
        };

        await _wineRepository.AddAsync(wine, cancellationToken);

        SetTerroirHighlight("wine", wine.Id);

        TempData["StatusMessage"] = $"Wine '{wine.Name}' was created.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWine(Guid id, [FromForm] UpdateWineRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _wineRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Wine could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            TempData["ErrorMessage"] = "Wine name is required.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        if (request.SubAppellationId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Please select a sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var subAppellation = await _subAppellationRepository.GetByIdAsync(request.SubAppellationId, cancellationToken);
        if (subAppellation is null)
        {
            TempData["ErrorMessage"] = "Selected sub-appellation could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var trimmedName = request.Name.Trim();
        var duplicate = await _wineRepository.FindByNameAsync(trimmedName, subAppellation.Name, subAppellation.Appellation?.Name, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            TempData["ErrorMessage"] = $"Wine '{trimmedName}' already exists for the selected sub-appellation.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        var grapeVariety = string.IsNullOrWhiteSpace(request.GrapeVariety) ? string.Empty : request.GrapeVariety.Trim();

        existing.Name = trimmedName;
        existing.GrapeVariety = grapeVariety;
        existing.Color = request.Color;
        existing.SubAppellationId = subAppellation.Id;
        existing.SubAppellation = subAppellation;

        await _wineRepository.UpdateAsync(existing, cancellationToken);

        SetTerroirHighlight("wine", existing.Id);

        TempData["StatusMessage"] = $"Wine '{existing.Name}' was updated.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWine(Guid id, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _wineRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "Wine could not be found.";
            return RedirectToAction(nameof(ManageTerroir));
        }

        await _wineRepository.DeleteAsync(id, cancellationToken);

        TempData["StatusMessage"] = $"Wine '{existing.Name}' was deleted.";
        return RedirectToAction(nameof(ManageTerroir));
    }

    [Authorize]
    [HttpPost("wines/merge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeWines([FromForm] MergeTerroirRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
        {
            return Forbid();
        }

        var validation = ValidateMergeRequest(request, WineMergeLabels);
        if (!validation.IsValid)
        {
            TempData["ErrorMessage"] = validation.ErrorMessage;
            return RedirectToAction(nameof(ManageTerroir));
        }

        try
        {
            var result = await _terroirMergeRepository.MergeWinesAsync(validation.LeaderId, validation.FollowerIds, cancellationToken);
            SetTerroirHighlight("wine", result.LeaderId);
            TempData["StatusMessage"] = CreateMergeStatusMessage(WineMergeLabels, result.LeaderName, result.FollowersMerged);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected wines. Please try again.";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "We couldn't merge the selected wines. Please try again.";
        }

        return RedirectToAction(nameof(ManageTerroir));
    }
    
     private async Task<WineSurferTerroirManagementViewModel> BuildTerroirManagementViewModel(
        WineSurferCurrentUser? currentUser,
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> incomingInvitations,
        IReadOnlyList<WineSurferSentInvitationNotification> sentInvitationNotifications,
        string? statusMessage,
        string? errorMessage,
        string? highlightSection,
        string? highlightId,
        CancellationToken cancellationToken)
    {
        var countries = await _countryRepository.GetAllAsync(cancellationToken);
        var regions = await _regionRepository.GetAllAsync(cancellationToken);
        var appellations = await _appellationRepository.GetAllAsync(cancellationToken);
        var subAppellations = await _subAppellationRepository.GetAllAsync(cancellationToken);
        var wines = await _wineRepository.GetAllAsync(cancellationToken);

        var regionCountByCountry = regions
            .GroupBy(region => region.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var appellationCountByCountry = appellations
            .Where(appellation => appellation.Region is not null)
            .GroupBy(appellation => appellation.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByCountry = subAppellations
            .Where(sub => sub.Appellation?.Region is not null)
            .GroupBy(sub => sub.Appellation!.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByCountry = wines
            .Where(wine => wine.SubAppellation?.Appellation?.Region is not null)
            .GroupBy(wine => wine.SubAppellation!.Appellation!.Region!.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        var countryModels = countries
            .Select(country =>
            {
                regionCountByCountry.TryGetValue(country.Id, out var regionCount);
                appellationCountByCountry.TryGetValue(country.Id, out var appellationCount);
                subAppellationCountByCountry.TryGetValue(country.Id, out var subAppellationCount);
                wineCountByCountry.TryGetValue(country.Id, out var wineCount);

                return new WineSurferTerroirCountry(
                    country.Id,
                    country.Name,
                    regionCount,
                    appellationCount,
                    subAppellationCount,
                    wineCount);
            })
            .OrderBy(country => country.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appellationCountByRegion = appellations
            .GroupBy(appellation => appellation.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByRegion = subAppellations
            .Where(sub => sub.Appellation?.Region is not null)
            .GroupBy(sub => sub.Appellation!.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByRegion = wines
            .Where(wine => wine.SubAppellation?.Appellation?.Region is not null)
            .GroupBy(wine => wine.SubAppellation!.Appellation!.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var subAppellationCountByAppellation = subAppellations
            .GroupBy(sub => sub.AppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountByAppellation = wines
            .Where(wine => wine.SubAppellation?.Appellation is not null)
            .GroupBy(wine => wine.SubAppellation!.AppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var wineCountBySubAppellation = wines
            .GroupBy(wine => wine.SubAppellationId)
            .ToDictionary(group => group.Key, group => group.Count());

        var regionModels = regions
            .Select(region =>
            {
                appellationCountByRegion.TryGetValue(region.Id, out var appellationCount);
                subAppellationCountByRegion.TryGetValue(region.Id, out var subAppellationCount);
                wineCountByRegion.TryGetValue(region.Id, out var wineCount);
                var countryName = region.Country?.Name;
                return new WineSurferTerroirRegion(
                    region.Id,
                    region.Name,
                    region.CountryId,
                    countryName,
                    appellationCount,
                    subAppellationCount,
                    wineCount);
            })
            .OrderBy(region => region.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appellationModels = appellations
            .Select(appellation =>
            {
                subAppellationCountByAppellation.TryGetValue(appellation.Id, out var subCount);
                wineCountByAppellation.TryGetValue(appellation.Id, out var wineCount);
                var regionName = appellation.Region?.Name ?? "Unknown region";
                var countryName = appellation.Region?.Country?.Name;
                return new WineSurferTerroirAppellation(
                    appellation.Id,
                    appellation.Name,
                    appellation.RegionId,
                    regionName,
                    countryName,
                    subCount,
                    wineCount);
            })
            .OrderBy(appellation => appellation.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subAppellationModels = subAppellations
            .Select(sub =>
            {
                wineCountBySubAppellation.TryGetValue(sub.Id, out var wineCount);
                var appellationName = sub.Appellation?.Name ?? "Unknown appellation";
                var regionName = sub.Appellation?.Region?.Name ?? "Unknown region";
                var countryName = sub.Appellation?.Region?.Country?.Name;
                return new WineSurferTerroirSubAppellation(
                    sub.Id,
                    sub.Name,
                    sub.AppellationId,
                    appellationName,
                    regionName,
                    countryName,
                    wineCount);
            })
            .OrderBy(sub => sub.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sub => sub.AppellationName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wineModels = wines
            .Select(wine =>
            {
                var subName = wine.SubAppellation?.Name ?? "Unknown sub-appellation";
                var appellationName = wine.SubAppellation?.Appellation?.Name ?? "Unknown appellation";
                var regionName = wine.SubAppellation?.Appellation?.Region?.Name ?? "Unknown region";
                var countryName = wine.SubAppellation?.Appellation?.Region?.Country?.Name;
                return new WineSurferTerroirWine(
                    wine.Id,
                    wine.Name,
                    wine.GrapeVariety,
                    wine.Color,
                    wine.SubAppellationId,
                    subName,
                    appellationName,
                    regionName,
                    countryName);
            })
            .OrderBy(wine => wine.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(wine => wine.SubAppellationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WineSurferTerroirManagementViewModel(
            currentUser,
            incomingInvitations,
            sentInvitationNotifications,
            countryModels,
            regionModels,
            appellationModels,
            subAppellationModels,
            wineModels,
            statusMessage,
            errorMessage,
            highlightSection,
            highlightId);
    }
     
    public record WineSurferTerroirManagementViewModel(
        WineSurferCurrentUser? CurrentUser,
        IReadOnlyList<WineSurferIncomingSisterhoodInvitation> IncomingInvitations,
        IReadOnlyList<WineSurferSentInvitationNotification> SentInvitationNotifications,
        IReadOnlyList<WineSurferTerroirCountry> Countries,
        IReadOnlyList<WineSurferTerroirRegion> Regions,
        IReadOnlyList<WineSurferTerroirAppellation> Appellations,
        IReadOnlyList<WineSurferTerroirSubAppellation> SubAppellations,
        IReadOnlyList<WineSurferTerroirWine> Wines,
        string? StatusMessage,
        string? ErrorMessage,
        string? HighlightSection,
        string? HighlightId);

    public record WineSurferTerroirCountry(
        Guid Id,
        string Name,
        int RegionCount,
        int AppellationCount,
        int SubAppellationCount,
        int WineCount);

    public record WineSurferTerroirRegion(
        Guid Id,
        string Name,
        Guid CountryId,
        string? CountryName,
        int AppellationCount,
        int SubAppellationCount,
        int WineCount);

    public record WineSurferTerroirAppellation(
        Guid Id,
        string Name,
        Guid RegionId,
        string RegionName,
        string? CountryName,
        int SubAppellationCount,
        int WineCount);

    public record WineSurferTerroirSubAppellation(
        Guid Id,
        string? Name,
        Guid AppellationId,
        string AppellationName,
        string RegionName,
        string? CountryName,
        int WineCount);

    public record WineSurferTerroirWine(
        Guid Id,
        string Name,
        string GrapeVariety,
        WineColor Color,
        Guid SubAppellationId,
        string SubAppellationName,
        string AppellationName,
        string RegionName,
        string? CountryName);
}

