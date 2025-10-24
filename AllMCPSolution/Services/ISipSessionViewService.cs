using System.Security.Claims;
using AllMCPSolution.Controllers;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public interface ISipSessionViewService
{
    Task<WineSurferSipSessionDetailViewModel> BuildSipSessionDetailViewModelAsync(
        SipSession session,
        ClaimsPrincipal user,
        Guid? currentUserId,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? foodSuggestions = null,
        string? foodSuggestionError = null,
        string? cheeseSuggestion = null);

    bool TryParseSipSessionFoodSuggestions(
        string? content,
        out IReadOnlyList<string> suggestions,
        out string? cheeseSuggestion);
}