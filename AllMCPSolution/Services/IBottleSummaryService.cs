using AllMCPSolution.Controllers;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public interface IBottleSummaryService
{
    IReadOnlyList<WineSurferSipSessionBottle> CreateFromSessionBottles(
        IEnumerable<SipSessionBottle>? sessionBottles,
        Guid? currentUserId = null,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null);

    IReadOnlyList<WineSurferSipSessionBottle> CreateFromBottles(
        IEnumerable<Bottle>? bottles,
        Guid? currentUserId = null,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null);
}