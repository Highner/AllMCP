using System.Collections.Generic;

namespace AllMCPSolution.Models;

public sealed record ActionMenuButtonModel(
    string MenuId,
    string TriggerAriaLabel,
    string MenuLabel,
    ActionMenuButtonEditOption Edit,
    ActionMenuButtonDeleteOption Delete,
    IReadOnlyList<ActionMenuButtonItem>? AdditionalItems = null,
    bool ShowEdit = true,
    bool ShowDelete = true
);

public sealed record ActionMenuButtonEditOption(
    string Label,
    IReadOnlyDictionary<string, string> Attributes,
    string? AdditionalCssClass = null
);

public sealed record ActionMenuButtonDeleteOption(
    string Label,
    string FormAction,
    IReadOnlyList<KeyValuePair<string, string>> HiddenFields,
    string? ConfirmMessage,
    string? ButtonCssClass = null,
    string? FormCssClass = null,
    bool IncludeAntiforgery = true,
    IReadOnlyDictionary<string, string>? FormAttributes = null,
    IReadOnlyDictionary<string, string>? ButtonAttributes = null
);

public sealed record ActionMenuButtonItem(
    ActionMenuButtonItemKind Kind,
    string Label,
    string? ButtonCssClass = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? FormAction = null,
    IReadOnlyList<KeyValuePair<string, string>>? HiddenFields = null,
    string? ConfirmMessage = null,
    string? Method = "post",
    string? FormCssClass = null,
    bool IncludeAntiforgery = true,
    IReadOnlyDictionary<string, string>? FormAttributes = null
);

public enum ActionMenuButtonItemKind
{
    Button,
    Form
}
