using System;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;

namespace AllMCPSolution.Services;

public interface IUserDrinkingWindowService
{
    Task SaveGeneratedWindowAsync(
        Guid userId,
        Guid wineVintageId,
        int startYear,
        int endYear,
        decimal alignmentScore,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class UserDrinkingWindowService : IUserDrinkingWindowService
{
    private readonly IWineVintageUserDrinkingWindowRepository _repository;

    public UserDrinkingWindowService(IWineVintageUserDrinkingWindowRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task SaveGeneratedWindowAsync(
        Guid userId,
        Guid wineVintageId,
        int startYear,
        int endYear,
        decimal alignmentScore,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existingWindow = await _repository.FindAsync(userId, wineVintageId, cancellationToken);
        if (existingWindow is null)
        {
            var newWindow = new WineVintageUserDrinkingWindow
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WineVintageId = wineVintageId,
                StartingYear = startYear,
                EndingYear = endYear,
                AlignmentScore = alignmentScore,
                GeneratedAtUtc = generatedAtUtc
            };

            await _repository.AddAsync(newWindow, cancellationToken);
            return;
        }

        existingWindow.StartingYear = startYear;
        existingWindow.EndingYear = endYear;
        existingWindow.AlignmentScore = alignmentScore;
        existingWindow.GeneratedAtUtc = generatedAtUtc;

        await _repository.UpdateAsync(existingWindow, cancellationToken);
    }
}
