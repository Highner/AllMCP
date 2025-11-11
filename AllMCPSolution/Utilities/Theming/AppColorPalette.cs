using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AllMCPSolution.Utilities.Theming;

public static class AppColorTokens
{
    public const string ColorScheme = "--wine-color-scheme";
    public const string Background = "--background";
    public const string Foreground = "--foreground";
    public const string Muted = "--muted";
    public const string MutedForeground = "--muted-foreground";
    public const string Accent = "--accent";
    public const string AccentForeground = "--accent-foreground";
    public const string Card = "--card";
    public const string CardForeground = "--card-foreground";
    public const string Popover = "--popover";
    public const string PopoverForeground = "--popover-foreground";
    public const string Primary = "--primary";
    public const string PrimaryForeground = "--primary-foreground";
    public const string Warning = "--warning";
    public const string WarningForeground = "--warning-foreground";
    public const string Secondary = "--secondary";
    public const string SecondaryForeground = "--secondary-foreground";
    public const string Destructive = "--destructive";
    public const string DestructiveForeground = "--destructive-foreground";
    public const string Positive = "--positive";
    public const string PositiveForeground = "--positive-foreground";
    public const string Border = "--border";
    public const string Input = "--input";
    public const string Ring = "--ring";
    public const string Radius = "--radius";
    public const string AppBackground = "--wine-app-background";
    public const string SurfaceGradient = "--wine-surface-gradient";
    public const string SurfaceGradientStrong = "--wine-surface-gradient-strong";
    public const string SurfaceBorder = "--wine-surface-border";
    public const string SurfaceBorderStrong = "--wine-surface-border-strong";
    public const string SurfaceHighlight = "--wine-surface-highlight";
    public const string SurfaceHighlightStrong = "--wine-surface-highlight-strong";
    public const string CardMaxWidth = "--wine-surfer-card-max-width";
}

public sealed class AppColorPalette
{
    public AppColorPalette(
        string name,
        string displayName,
        IDictionary<string, string> cssVariables,
        IReadOnlyList<string> chartColors,
        bool isDefault = false,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Palette name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Palette display name is required.", nameof(displayName));
        }

        if (cssVariables is null || cssVariables.Count == 0)
        {
            throw new ArgumentException("At least one CSS variable must be provided.", nameof(cssVariables));
        }

        if (chartColors is null || chartColors.Count == 0)
        {
            throw new ArgumentException("At least one chart color must be provided.", nameof(chartColors));
        }

        Name = name.Trim();
        DisplayName = displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsDefault = isDefault;
        CssVariables = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(cssVariables, StringComparer.Ordinal));
        ChartColors = new ReadOnlyCollection<string>(chartColors.ToList());
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public bool IsDefault { get; }

    public IReadOnlyDictionary<string, string> CssVariables { get; }

    public IReadOnlyList<string> ChartColors { get; }
}

public static class AppColorPalettes
{
    public static IReadOnlyList<AppColorPalette> DefaultPalettes { get; } = CreateDefaultPalettes();

    private static IReadOnlyList<AppColorPalette> CreateDefaultPalettes()
    {
        return new List<AppColorPalette>
        {
            CreateNocturnePalette(),
            CreateCoastalPalette(),
            CreateDaybreakPalette()
        };
    }

    private static AppColorPalette CreateNocturnePalette()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppColorTokens.ColorScheme] = "dark",
            [AppColorTokens.Background] = "20 14.3% 4.1%",
            [AppColorTokens.Foreground] = "60 9.1% 97.8%",
            [AppColorTokens.Muted] = "20 14.3% 14.1%",
            [AppColorTokens.MutedForeground] = "60 4.8% 72.9%",
            [AppColorTokens.Accent] = "20 14.3% 16.1%",
            [AppColorTokens.AccentForeground] = "60 9.1% 97.8%",
            [AppColorTokens.Card] = "20 14.3% 6.1%",
            [AppColorTokens.CardForeground] = "60 9.1% 97.8%",
            [AppColorTokens.Popover] = "20 14.3% 6.1%",
            [AppColorTokens.PopoverForeground] = "60 9.1% 97.8%",
            [AppColorTokens.Primary] = "24 94% 62%",
            [AppColorTokens.PrimaryForeground] = "60 9.1% 9.8%",
            [AppColorTokens.Warning] = "37 92% 58%",
            [AppColorTokens.WarningForeground] = "45 100% 10%",
            [AppColorTokens.Secondary] = "20 14.3% 10.1%",
            [AppColorTokens.SecondaryForeground] = "60 9.1% 97.8%",
            [AppColorTokens.Destructive] = "0 62.8% 45.6%",
            [AppColorTokens.DestructiveForeground] = "60 9.1% 97.8%",
            [AppColorTokens.Positive] = "142 71% 36%",
            [AppColorTokens.PositiveForeground] = "142 70% 92%",
            [AppColorTokens.Border] = "20 14.3% 24.1%",
            [AppColorTokens.Input] = "20 14.3% 24.1%",
            [AppColorTokens.Ring] = "24 94% 62%",
            [AppColorTokens.Radius] = "0.95rem",
            [AppColorTokens.AppBackground] = "radial-gradient(circle at top, rgba(244, 241, 234, 0.08), transparent 60%), #050505",
            [AppColorTokens.SurfaceGradient] = "linear-gradient(160deg, rgba(244, 241, 234, 0.06), rgba(244, 241, 234, 0.02))",
            [AppColorTokens.SurfaceGradientStrong] = "linear-gradient(150deg, rgba(244, 241, 234, 0.08), rgba(244, 241, 234, 0.02))",
            [AppColorTokens.SurfaceBorder] = "rgba(244, 241, 234, 0.08)",
            [AppColorTokens.SurfaceBorderStrong] = "rgba(244, 241, 234, 0.12)",
            [AppColorTokens.SurfaceHighlight] = "radial-gradient(circle at top right, rgba(244, 241, 234, 0.12), transparent 55%)",
            [AppColorTokens.SurfaceHighlightStrong] = "radial-gradient(circle at top right, rgba(244, 241, 234, 0.18), transparent 55%)",
            [AppColorTokens.CardMaxWidth] = "960px"
        };

        var chartColors = new List<string>
        {
            "#E4572E",
            "#4E79A7",
            "#F28E2B",
            "#76B7B2",
            "#59A14F",
            "#EDC948",
            "#B07AA1",
            "#FF9DA7",
            "#9C755F",
            "#BAB0AC"
        };

        return new AppColorPalette(
            "nocturne",
            "Nocturne Noir",
            variables,
            chartColors,
            isDefault: true,
            description: "Deep charcoal base with warm copper highlights.");
    }

    private static AppColorPalette CreateCoastalPalette()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppColorTokens.ColorScheme] = "dark",
            [AppColorTokens.Background] = "210 28% 10%",
            [AppColorTokens.Foreground] = "210 33% 96%",
            [AppColorTokens.Muted] = "210 22% 18%",
            [AppColorTokens.MutedForeground] = "210 25% 74%",
            [AppColorTokens.Accent] = "204 32% 22%",
            [AppColorTokens.AccentForeground] = "210 33% 96%",
            [AppColorTokens.Card] = "210 24% 14%",
            [AppColorTokens.CardForeground] = "210 33% 96%",
            [AppColorTokens.Popover] = "210 24% 14%",
            [AppColorTokens.PopoverForeground] = "210 33% 96%",
            [AppColorTokens.Primary] = "197 89% 66%",
            [AppColorTokens.PrimaryForeground] = "205 82% 12%",
            [AppColorTokens.Warning] = "34 91% 62%",
            [AppColorTokens.WarningForeground] = "24 90% 14%",
            [AppColorTokens.Secondary] = "215 22% 20%",
            [AppColorTokens.SecondaryForeground] = "210 33% 96%",
            [AppColorTokens.Destructive] = "352 74% 58%",
            [AppColorTokens.DestructiveForeground] = "0 0% 100%",
            [AppColorTokens.Positive] = "157 64% 42%",
            [AppColorTokens.PositiveForeground] = "156 62% 92%",
            [AppColorTokens.Border] = "210 30% 32%",
            [AppColorTokens.Input] = "210 30% 32%",
            [AppColorTokens.Ring] = "197 89% 66%",
            [AppColorTokens.Radius] = "0.95rem",
            [AppColorTokens.AppBackground] = "radial-gradient(circle at top, rgba(168, 210, 232, 0.08), transparent 60%), #040b12",
            [AppColorTokens.SurfaceGradient] = "linear-gradient(165deg, rgba(168, 210, 232, 0.1), rgba(168, 210, 232, 0.03))",
            [AppColorTokens.SurfaceGradientStrong] = "linear-gradient(150deg, rgba(168, 210, 232, 0.14), rgba(168, 210, 232, 0.04))",
            [AppColorTokens.SurfaceBorder] = "rgba(168, 210, 232, 0.14)",
            [AppColorTokens.SurfaceBorderStrong] = "rgba(168, 210, 232, 0.22)",
            [AppColorTokens.SurfaceHighlight] = "radial-gradient(circle at top right, rgba(168, 210, 232, 0.18), transparent 55%)",
            [AppColorTokens.SurfaceHighlightStrong] = "radial-gradient(circle at top right, rgba(168, 210, 232, 0.26), transparent 55%)",
            [AppColorTokens.CardMaxWidth] = "960px"
        };

        var chartColors = new List<string>
        {
            "#4FB0C6",
            "#508991",
            "#172A3A",
            "#004346",
            "#09BC8A",
            "#F4A259",
            "#5C6BC0",
            "#BC6FF1",
            "#FFB4A2",
            "#FFD166"
        };

        return new AppColorPalette(
            "coastal",
            "Coastal Dawn",
            variables,
            chartColors,
            description: "Ocean blues with sunrise accents.");
    }

    private static AppColorPalette CreateDaybreakPalette()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppColorTokens.ColorScheme] = "light",
            [AppColorTokens.Background] = "38 42% 96%",
            [AppColorTokens.Foreground] = "24 32% 18%",
            [AppColorTokens.Muted] = "38 30% 88%",
            [AppColorTokens.MutedForeground] = "24 22% 38%",
            [AppColorTokens.Accent] = "156 32% 82%",
            [AppColorTokens.AccentForeground] = "156 40% 22%",
            [AppColorTokens.Card] = "38 38% 98%",
            [AppColorTokens.CardForeground] = "24 32% 18%",
            [AppColorTokens.Popover] = "34 40% 99%",
            [AppColorTokens.PopoverForeground] = "24 32% 18%",
            [AppColorTokens.Primary] = "27 82% 62%",
            [AppColorTokens.PrimaryForeground] = "26 32% 16%",
            [AppColorTokens.Warning] = "40 88% 58%",
            [AppColorTokens.WarningForeground] = "28 40% 18%",
            [AppColorTokens.Secondary] = "158 34% 84%",
            [AppColorTokens.SecondaryForeground] = "160 42% 24%",
            [AppColorTokens.Destructive] = "6 74% 56%",
            [AppColorTokens.DestructiveForeground] = "0 0% 98%",
            [AppColorTokens.Positive] = "143 48% 48%",
            [AppColorTokens.PositiveForeground] = "141 50% 94%",
            [AppColorTokens.Border] = "37 24% 78%",
            [AppColorTokens.Input] = "37 24% 78%",
            [AppColorTokens.Ring] = "27 82% 62%",
            [AppColorTokens.Radius] = "0.95rem",
            [AppColorTokens.AppBackground] = "radial-gradient(circle at top, rgba(255, 233, 214, 0.6), transparent 60%), #f6f1ea",
            [AppColorTokens.SurfaceGradient] = "linear-gradient(160deg, rgba(255, 233, 214, 0.45), rgba(200, 225, 215, 0.24))",
            [AppColorTokens.SurfaceGradientStrong] = "linear-gradient(150deg, rgba(255, 233, 214, 0.58), rgba(200, 225, 215, 0.3))",
            [AppColorTokens.SurfaceBorder] = "rgba(96, 84, 72, 0.14)",
            [AppColorTokens.SurfaceBorderStrong] = "rgba(96, 84, 72, 0.22)",
            [AppColorTokens.SurfaceHighlight] = "radial-gradient(circle at top right, rgba(200, 225, 215, 0.42), transparent 60%)",
            [AppColorTokens.SurfaceHighlightStrong] = "radial-gradient(circle at top right, rgba(200, 225, 215, 0.55), transparent 55%)",
            [AppColorTokens.CardMaxWidth] = "960px"
        };

        var chartColors = new List<string>
        {
            "#F4A259",
            "#FFCF99",
            "#7BC8A4",
            "#A3CEF1",
            "#D4A5A5",
            "#F6D6AD",
            "#B7B7A4",
            "#E9B3CE",
            "#9BD0D9",
            "#E2C2FF"
        };

        return new AppColorPalette(
            "daybreak",
            "Daybreak Bloom",
            variables,
            chartColors,
            description: "Soft daylight neutrals with gentle pastels.");
    }
}
