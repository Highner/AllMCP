using System;
using Microsoft.AspNetCore.Html;

namespace AllMCPSolution.Models;

public class CrudTableTemplateModel
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string DataTableKey { get; init; } = string.Empty;

    public string BackgroundCssClass { get; init; } = "crud-table wine-surface wine-surface-border wine-card-hover";

    public string BodyCssClass { get; init; } = "crud-table__body wine-surface wine-surface-border";

    public Func<object?, IHtmlContent>? TitleContent { get; init; }

    public Func<object?, IHtmlContent>? HeaderContent { get; init; }

    public Func<object?, IHtmlContent>? BodyContent { get; init; }
}
