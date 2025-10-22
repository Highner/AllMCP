using System.Collections.Generic;
using Microsoft.AspNetCore.Html;

namespace AllMCPSolution.Models;

public record WineSurferModalShell
{
    public string? BackdropId { get; init; }
    public string BackdropCssClass { get; init; } = "wine-surfer-modal-backdrop";
    public IDictionary<string, string?>? BackdropAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public string? DialogId { get; init; }
    public string DialogCssClass { get; init; } = "wine-surfer-modal";
    public IDictionary<string, string?>? DialogAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public string HeaderCssClass { get; init; } = "wine-surfer-modal__header";
    public IDictionary<string, string?>? HeaderAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public string Title { get; init; } = string.Empty;
    public string TitleCssClass { get; init; } = "wine-surfer-modal__title";
    public string? TitleElementId { get; init; }
    public string TitleTagName { get; init; } = "h2";
    public IDictionary<string, string?>? TitleAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public bool RenderHeader { get; init; } = true;
    public bool RenderCloseButton { get; init; } = true;
    public string CloseButtonCssClass { get; init; } = "wine-surfer-modal__close";
    public IDictionary<string, string?>? CloseButtonAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public string CloseButtonLabel { get; init; } = "Close dialog";
    public string CloseButtonContent { get; init; } = "×";
    public string BodyCssClass { get; init; } = "wine-surfer-modal__body";
    public IDictionary<string, string?>? BodyAttributes { get; init; }
        = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
    public IHtmlContent? BodyContent { get; init; }
}
