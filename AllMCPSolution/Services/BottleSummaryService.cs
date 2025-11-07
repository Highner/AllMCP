using AllMCPSolution.Controllers;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public sealed class BottleSummaryService : IBottleSummaryService
{
    public IReadOnlyList<WineSurferSipSessionBottle> CreateFromSessionBottles(
        IEnumerable<SipSessionBottle>? sessionBottles,
        Guid? currentUserId = null,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null)
    {
        return CreateBottleSummariesInternal(
            sessionBottles,
            link => link?.Bottle,
            link => link?.IsRevealed ?? false,
            currentUserId,
            sisterhoodAverageScores);
    }

    public IReadOnlyList<WineSurferSipSessionBottle> CreateFromBottles(
        IEnumerable<Bottle>? bottles,
        Guid? currentUserId = null,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores = null)
    {
        return CreateBottleSummariesInternal(
            bottles,
            bottle => bottle,
            _ => true,
            currentUserId,
            sisterhoodAverageScores);
    }

    private static IReadOnlyList<WineSurferSipSessionBottle> CreateBottleSummariesInternal<T>(
        IEnumerable<T>? source,
        Func<T, Bottle?> bottleSelector,
        Func<T, bool> isRevealedSelector,
        Guid? currentUserId,
        IReadOnlyDictionary<Guid, decimal>? sisterhoodAverageScores)
    {
        if (source is null)
        {
            return Array.Empty<WineSurferSipSessionBottle>();
        }

        var summaries = source
            .Where(entry => entry is not null)
            .Select(entry =>
            {
                var bottle = bottleSelector(entry!);
                if (bottle is null)
                {
                    return null;
                }

                var actualIsRevealed = isRevealedSelector(entry!);
                var isOwnedByCurrentUser = currentUserId.HasValue &&
                                           bottle.UserId.HasValue &&
                                           bottle.UserId.Value == currentUserId.Value;

                var labelBase = CreateBottleLabel(bottle);
                var rawWineName = bottle.WineVintage?.Wine?.Name;
                var wineName = string.IsNullOrWhiteSpace(rawWineName)
                    ? "Bottle"
                    : rawWineName!.Trim();

                var vintageValue = bottle.WineVintage?.Vintage;

                if (!actualIsRevealed && !isOwnedByCurrentUser)
                {
                    wineName = "Mystery bottle";
                    labelBase = "Mystery bottle";
                    vintageValue = null;
                }

                TastingNote? currentUserNote = null;

                if (currentUserId.HasValue)
                {
                    currentUserNote = bottle.TastingNotes?
                        .FirstOrDefault(note => note.UserId == currentUserId.Value);
                }

                var scoreValues = bottle.TastingNotes?
                    .Where(note => note.Score.HasValue)
                    .Select(note => note.Score!.Value)
                    .ToList();

                decimal? averageScore = null;
                if (scoreValues is { Count: > 0 })
                {
                    averageScore = scoreValues.Average();
                }

                decimal? sisterhoodAverageScore = null;
                var wineVintageId = bottle.WineVintageId;
                if (wineVintageId != Guid.Empty && sisterhoodAverageScores is not null &&
                    sisterhoodAverageScores.TryGetValue(wineVintageId, out var average))
                {
                    sisterhoodAverageScore = average;
                }

                if (!actualIsRevealed && !isOwnedByCurrentUser)
                {
                    averageScore = null;
                    sisterhoodAverageScore = null;
                }

                var currentUserMarkedNotTasted = currentUserNote?.NotTasted ?? false;

                return new WineSurferSipSessionBottle(
                    bottle.Id,
                    wineName,
                    vintageValue,
                    labelBase,
                    isOwnedByCurrentUser,
                    bottle.PendingDelivery,
                    bottle.IsDrunk,
                    bottle.DrunkAt,
                    currentUserNote?.Id,
                    currentUserNote?.Note,
                    currentUserMarkedNotTasted,
                    currentUserNote?.Score,
                    averageScore,
                    sisterhoodAverageScore,
                    actualIsRevealed);
            })
            .Where(summary => summary is not null)
            .OrderBy(summary => summary!.Label, StringComparer.OrdinalIgnoreCase)
            .Select(summary => summary!)
            .ToList();

        if (summaries.Count == 0)
        {
            return Array.Empty<WineSurferSipSessionBottle>();
        }

        return summaries;
    }

    private static string CreateBottleLabel(Bottle bottle)
    {
        var wineName = bottle.WineVintage?.Wine?.Name;
        var labelBase = string.IsNullOrWhiteSpace(wineName)
            ? "Bottle"
            : wineName!.Trim();

        var vintage = bottle.WineVintage?.Vintage;
        if (vintage.HasValue && vintage.Value > 0)
        {
            labelBase = $"{labelBase} {vintage.Value}";
        }

        return labelBase;
    }
}