using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Html;

namespace AllMCPSolution.Models;

public class BottleManagementModalViewModel
{
    public static BottleManagementModalViewModel Default { get; } = new();

    public string OverlayId { get; init; } = "bottle-management-overlay";

    public string OverlayCssClass { get; init; } = "wine-surfer-modal-backdrop bottle-management-overlay";

    public IDictionary<string, string?>? OverlayAttributes { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["hidden"] = null,
            ["aria-hidden"] = "true"
        };

    public string DialogId { get; init; } = "bottle-management-modal";

    public string DialogCssClass { get; init; } = "wine-surfer-modal bottle-management-modal";

    public IDictionary<string, string?>? DialogAttributes { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string BodyCssClass { get; init; } = "wine-surfer-modal__body bottle-management-modal__body";

    public IDictionary<string, string?>? BodyAttributes { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string TitleElementId { get; init; } = "details-title";

    public string CloseButtonAriaLabel { get; init; } = "Close bottle details dialog";

    public Func<object?, IHtmlContent>? HeaderContent { get; init; }
        = null;
}
