using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public class InventoryAddFormPartialModel
{
    public IEnumerable<BottleLocationOption> Locations { get; init; } = Array.Empty<BottleLocationOption>();

    public string FormClass { get; init; } = "inventory-add-form";

    public bool UseNoValidate { get; init; }

    public string IntroClass { get; init; } = "inventory-add-intro";

    public string IntroText { get; init; } = "Choose an existing wine and vintage to add a bottle to your cellar.";

    public string WineFieldClass { get; init; } = "inventory-add-field";

    public string WineLabelClass { get; init; } = "inventory-add-label";

    public string WineLabelText { get; init; } = "Wine";

    public string ComboboxClass { get; init; } = "inventory-add-combobox";

    public string SearchInputClass { get; init; } = "inventory-add-wine-search";

    public IDictionary<string, string?> ComboboxAttributes { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string SearchInputPlaceholder { get; init; } = "Search for a wine";

    public string SearchInputAriaLabel { get; init; } = "Wine";

    public string SearchHiddenInputClass { get; init; } = "inventory-add-wine-id";

    public string ResultsContainerClass { get; init; } = "inventory-add-wine-results";

    public string ResultsContainerId { get; init; } = "inventory-add-wine-results";

    public string ResultsAriaLabel { get; init; } = "Matching wines";

    public string SummaryClass { get; init; } = "inventory-add-summary";

    public string SummaryText { get; init; } = "Search for a wine to see its appellation and color.";

    public string SummaryAriaLive { get; init; } = "polite";

    public string VintageFieldClass { get; init; } = "inventory-add-field";

    public string VintageLabelClass { get; init; } = "inventory-add-label";

    public string VintageLabelText { get; init; } = "Vintage";

    public string VintageInputClass { get; init; } = "inventory-add-vintage";

    public string VintageHintClass { get; init; } = "inventory-add-vintage-hint";

    public string? VintageHintText { get; init; } = "Search for a wine to view existing vintages.";

    public string VintageHintAriaLive { get; init; } = "polite";

    public string LocationFieldClass { get; init; } = "inventory-add-field";

    public string LocationLabelClass { get; init; } = "inventory-add-label";

    public string LocationLabelText { get; init; } = "Location";

    public string LocationSelectClass { get; init; } = "inventory-add-location";

    public string QuantityFieldClass { get; init; } = "inventory-add-field";

    public string QuantityLabelClass { get; init; } = "inventory-add-label";

    public string QuantityLabelText { get; init; } = "Number of Bottles";

    public string QuantitySelectClass { get; init; } = "inventory-add-quantity";

    public bool ShowLocation { get; init; } = true;

    public bool ShowQuantity { get; init; } = true;

    public string ErrorClass { get; init; } = "inventory-add-error";

    public string ErrorRole { get; init; } = "alert";

    public string ErrorAriaHidden { get; init; } = "true";

    public string ActionsClass { get; init; } = "inventory-add-actions";

    public string CancelButtonClass { get; init; } = "crud-table__action-button secondary inventory-add-cancel";

    public string CancelButtonText { get; init; } = "Cancel";

    public string SubmitButtonClass { get; init; } = "crud-table__action-button inventory-add-submit";

    public string SubmitButtonText { get; init; } = "Add Wine";

    public string? AdditionalFieldsPartial { get; init; }

    public object? AdditionalFieldsModel { get; init; }
}
