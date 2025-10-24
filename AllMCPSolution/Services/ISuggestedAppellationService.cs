using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Controllers; // For WineSurferSuggestedAppellation and WineSurferSuggestedWine records

namespace AllMCPSolution.Services;

public interface ISuggestedAppellationService
{
    Task<IReadOnlyList<WineSurferSuggestedAppellation>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
}