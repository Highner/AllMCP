using System;

namespace AllMCPSolution.Models;

public class BottleLocationOption
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Capacity { get; set; }
}
