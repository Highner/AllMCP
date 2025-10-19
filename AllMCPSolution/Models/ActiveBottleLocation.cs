using System;

namespace AllMCPSolution.Models;

public sealed record ActiveBottleLocation(
    Guid? RegionId,
    string? RegionName,
    Guid? AppellationId,
    string? AppellationName,
    int? Vintage);
