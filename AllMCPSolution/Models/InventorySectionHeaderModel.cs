using System;
using Microsoft.AspNetCore.Mvc.Razor;

namespace AllMCPSolution.Models;

public class InventorySectionHeaderModel
{
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? AdditionalCssClass { get; init; }

    public Func<object, HelperResult>? Actions { get; init; }
}
