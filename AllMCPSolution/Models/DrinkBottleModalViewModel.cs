using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public record DrinkBottleModalHiddenField
{
    public string Name { get; init; }

    public string? Value { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; }

    public DrinkBottleModalHiddenField(string name, string? value = null, IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Hidden field name must be provided.", nameof(name));
        }

        Name = name;
        Value = value;
        Attributes = attributes ?? new Dictionary<string, string>();
    }
}

public record DrinkBottleModalViewModel
{
    public static DrinkBottleModalViewModel Default { get; } = new();

    public string Title { get; init; } = "Drink Bottle";

    public string Intro { get; init; } = "Record when and how you enjoyed this bottle.";

    public string SubmitLabel { get; init; } = "Drink Bottle";

    public string CancelLabel { get; init; } = "Cancel";

    public string? FormAction { get; init; }

    public string FormMethod { get; init; } = "post";

    public bool IncludeAntiForgeryToken { get; init; }

    public bool ShowDate { get; init; } = true;

    public bool RequireDate { get; init; } = true;

    public bool ShowScore { get; init; } = true;

    public bool RequireNote { get; init; } = false;

    public string DateLabel { get; init; } = "Date";

    public string ScoreLabel { get; init; } = "Score";

    public string NoteLabel { get; init; } = "Tasting Note";

    public string DateInputAriaLabel { get; init; } = "Drinking date";

    public string ScoreInputAriaLabel { get; init; } = "Score";

    public string NoteInputAriaLabel { get; init; } = "Tasting note";

    public string OverlayId { get; init; } = "drink-bottle-overlay";

    public string PopoverId { get; init; } = "drink-bottle-popover";

    public string TitleElementId { get; init; } = "drink-bottle-title";

    public string FormCssClass { get; init; } = "drink-bottle-form";

    public string SubmitButtonCssClass { get; init; } = "crud-table__action-button drink-bottle-submit";

    public string CancelButtonCssClass { get; init; } = "crud-table__action-button secondary drink-bottle-cancel";

    public int NoteRows { get; init; } = 4;

    public IReadOnlyList<DrinkBottleModalHiddenField> HiddenFields { get; init; } = Array.Empty<DrinkBottleModalHiddenField>();

    public IReadOnlyDictionary<string, string> FormAttributes { get; init; } = new Dictionary<string, string>();
}
