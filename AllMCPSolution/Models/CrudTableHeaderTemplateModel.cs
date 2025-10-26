using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AllMCPSolution.Models;

public class CrudTableHeaderTemplateModel
{
    public string ContainerTagName { get; init; } = "div";

    public string ContainerCssClass { get; init; } = "crud-table__header-actions";

    public IReadOnlyDictionary<string, string?>? ContainerAttributes { get; init; }
        = new Dictionary<string, string?>();

    public string ActionTagName { get; init; } = "button";

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionCssClass { get; init; } = "sisterhood-button";

    public string ActionType { get; init; } = "button";

    public IReadOnlyDictionary<string, string?>? ActionAttributes { get; init; }
        = new Dictionary<string, string?>();

    public bool HasContent => !string.IsNullOrWhiteSpace(ActionLabel);

    public Func<object?, IHtmlContent> Render => _ => BuildContent();

    private IHtmlContent BuildContent()
    {
        if (!HasContent)
        {
            return HtmlString.Empty;
        }

        var container = new TagBuilder(ContainerTagName);
        if (!string.IsNullOrWhiteSpace(ContainerCssClass))
        {
            container.AddCssClass(ContainerCssClass);
        }

        MergeAttributes(container, ContainerAttributes);

        var action = new TagBuilder(ActionTagName);
        if (!string.IsNullOrWhiteSpace(ActionCssClass))
        {
            action.AddCssClass(ActionCssClass);
        }

        if (string.Equals(ActionTagName, "button", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ActionType))
        {
            action.MergeAttribute("type", ActionType, true);
        }

        MergeAttributes(action, ActionAttributes);

        action.InnerHtml.Append(ActionLabel);

        container.InnerHtml.AppendHtml(action);

        return container;
    }

    private static void MergeAttributes(TagBuilder tagBuilder, IReadOnlyDictionary<string, string?>? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var pair in attributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var value = pair.Value ?? string.Empty;
            tagBuilder.MergeAttribute(pair.Key, value, replaceExisting: true);
        }
    }
}
