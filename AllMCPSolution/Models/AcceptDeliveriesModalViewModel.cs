using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public class AcceptDeliveriesModalViewModel
{
    public static AcceptDeliveriesModalViewModel Empty { get; } = new();

    public IReadOnlyList<PendingBottleOption> PendingBottles { get; init; } = Array.Empty<PendingBottleOption>();

    public IReadOnlyList<BottleLocationOption> Locations { get; init; } = Array.Empty<BottleLocationOption>();
}

public class PendingBottleOption
{
    public Guid BottleId { get; init; }

    public Guid WineVintageId { get; init; }

    public string WineName { get; init; } = string.Empty;

    public int Vintage { get; init; }

    public string? SubAppellation { get; init; }

    public string? Appellation { get; init; }

    public string? Region { get; init; }

    public string? LocationName { get; init; }
}
