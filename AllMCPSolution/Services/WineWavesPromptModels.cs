using System;
using System.Collections.Generic;

namespace AllMCPSolution.Services;

public sealed record WineWavesPromptItem(
    Guid WineVintageId,
    string Label,
    int Vintage,
    string? Origin,
    string? Attributes,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<WineWavesPromptScore> ExistingScores)
{
    public int? DrinkingWindowStartYear { get; init; }
    public int? DrinkingWindowEndYear { get; init; }
}

public sealed record WineWavesPromptScore(int Year, decimal Score);
