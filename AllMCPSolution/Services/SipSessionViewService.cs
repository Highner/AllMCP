using System.Security.Claims;
using AllMCPSolution.Controllers;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public sealed class SipSessionViewService : ISipSessionViewService
{
    public Task<WineSurferSipSessionDetailViewModel> BuildSipSessionDetailViewModelAsync(
        SipSession session,
        ClaimsPrincipal user,
        Guid? currentUserId,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? foodSuggestions = null,
        string? foodSuggestionError = null,
        string? cheeseSuggestion = null)
    {
        throw new NotImplementedException("SipSessionViewService is not implemented. Use controller-specific implementation.");
    }

    public bool TryParseSipSessionFoodSuggestions(
        string? content,
        out IReadOnlyList<string> suggestions,
        out string? cheeseSuggestion)
    {
        throw new NotImplementedException("SipSessionViewService is not implemented. Use controller-specific implementation.");
    }
}