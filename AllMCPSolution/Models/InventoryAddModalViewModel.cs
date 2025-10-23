using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public class InventoryAddModalViewModel
{
    public IReadOnlyList<BottleLocationOption> Locations { get; init; } = Array.Empty<BottleLocationOption>();
}
