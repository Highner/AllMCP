using System;
using Microsoft.AspNetCore.Html;

namespace AllMCPSolution.Models;

public class InventorySectionHeaderModel
{
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? AdditionalCssClass { get; init; }

    public Func<object?, IHtmlContent>? Actions { get; init; }
}
