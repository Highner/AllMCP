using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public interface IChatGptPromptService
{
    string TasteProfileGenerationSystemPrompt { get; }

    string SurfEyeSystemPrompt { get; }

    string SipSessionFoodSuggestionSystemPrompt { get; }

    string WineWavesSystemPrompt { get; }
    
    string DrinkingWindowSystemPrompt { get; }

    string BuildTasteProfilePrompt(
        IReadOnlyList<(Bottle Bottle, TastingNote Note)> scoredBottles,
        decimal? suggestionBudget);

    string BuildSurfEyePrompt(string? tasteProfileSummary, string tasteProfile);

    string BuildSurfEyePromptWithoutTasteProfile();

    string BuildSurfEyeIdentificationPrompt();

    string BuildSipSessionFoodSuggestionPrompt(SipSession session);

    string BuildWineWavesPrompt(
        IReadOnlyList<WineWavesPromptItem> vintages,
        string? tasteProfileSummary,
        string? tasteProfile);

    string BuildDrinkingWindowPrompt(string tasteProfile, string wineDescription);
}

public class ChatGptPromptService : IChatGptPromptService
{
    private const string DrinkingWindowSystemPromptText =
        "YRespond ONLY with minified JSON matching {\\\"startYear\\\":2000,\\\"endYear\\\":2010}. " +
        "Both startYear and endYear must be integers representing the inclusive start and end of the optimal drinking window. Do not include any commentary or code fences.";
    
    private const string TasteProfileSystemPromptText =
        "You are an expert sommelier assistant. Respond ONLY with valid minified JSON like {\\\"summary\\\":\\\"...\\\",\\\"profile\\\":\\\"...\\\",\\\"suggestedAppellations\\\":[{\\\"country\\\":\\\"...\\\",\\\"region\\\":\\\"...\\\",\\\"appellation\\\":\\\"...\\\",\\\"subAppellation\\\":null,\\\"reason\\\":\\\"...\\\",\\\"wines\\\":[{\\\"name\\\":\\\"...\\\",\\\"color\\\":\\\"Red\\\",\\\"variety\\\":\\\"...\\\",\\\"subAppellation\\\":null,\\\"vintage\\\":\\\"2019\\\"},{\\\"name\\\":\\\"...\\\",\\\"color\\\":\\\"White\\\",\\\"variety\\\":null,\\\"subAppellation\\\":null,\\\"vintage\\\":\\\"NV\\\"}]}]}. " +
        "The summary must be 200 characters or fewer and offer a concise descriptor of the user's palate. " +
        "The profile must be structured as a list of ‘green-light’ descriptors to embrace and ‘red-light’ descriptors to avoid, grouped by sensory themes without recommending specific new wines. " +
        "The suggestedAppellations array must contain exactly two entries describing appellations or sub-appellations that fit the profile, each with country, region, appellation strings, subAppellation set to a string or null, and a single-sentence reason of 200 characters or fewer explaining the match. " +
        "For each suggested appellation, include a wines array with two or three entries highlighting wines from that location. Each wine must provide the full label name (producer and climat, if applicable) without repeating grape varieties, regions, or appellation names, a color of Red, White, or Rose, an optional variety string or null, an optional subAppellation or null, and a vintage string that is either a 4-digit year or \\\"NV\\\". " +
        "Do not include code fences, or any explanatory text outside the JSON object. Use markdown in the profile field only.";

    private const string SurfEyeSystemPromptText = """
You are Surf Eye, an expert sommelier and computer vision guide. Use the user's taste profile to rank wines from best to worst alignment.
Respond ONLY with minified JSON matching {"analysisSummary":"...","wines":[{"name":"...","producer":"...","country":"...","region":"...","appellation":"...","subAppellation":"...","variety":"...","color":"Red","vintage":"...","alignmentScore":0,"alignmentSummary":"...","confidence":0.0,"notes":"..."}]}. 
The wines array must be sorted by descending alignmentScore. Include at most five wines. Provide concise notes that justify the ranking with respect to the taste profile. Report each wine's color using Red, White, or Rose and set any unknown fields to null. The alignmentScore must be an integer from 0 to 10. Confidence must be a decimal between 0 and 1. If no wine is recognized, return an empty wines array and set analysisSummary to a short explanation. Do not use markdown, newlines, or any commentary outside of the JSON object.
For each wine entry, ensure the name field excludes grape varieties and vintage information; capture those details only in the designated fields.
""";

    private const string SipSessionFoodSuggestionSystemPromptText = """
You are an expert sommelier assistant. Recommend three distinct food pairings that can be served together with the wines provided by the user.
Ensure at least one suggestion is vegetarian and begin that entry with "(Vegetarian)".
Respond ONLY with minified JSON matching {"suggestions":["Suggestion 1","Suggestion 2","Suggestion 3"],"cheese":"Cheese course"}.
Each suggestion must be a short dish description followed by a concise reason, and the cheese field must describe a dedicated cheese course pairing. Do not include numbering, markdown, or any other fields.
""";

    private const string WineWavesSystemPromptText = """
You are an expert sommellier.

Project the evolution of wines over time and how they align with the user's taste preferences on an absolute scale of 0-10. Provide no descriptions—only scores.
Respond ONLY with minified JSON matching {"vintages":[{"wineVintageId":"...","CriticsAssessment":"...","scores":[{"year":2024,"score":7.4}]}]}.
Only include wineVintageId values provided by the user and omit any commentary outside the JSON payload.

Start the scoring timeline three calendar years after each wine's actual vintage. The goal is to provide a comprehensive picture of the wine's evolution over time in a nice curve. Avoid straight lines.

For each wine, crosscheck the user's taste profile and publicly available tasting notes and descriptions (preferably from professional critics) from the web to determine the wine's alignment score.
Pay special attention to assessments about the wine's development over the course of its life.

Provide consecutive annual scores for at least 20 years starting from that year.
If the wine is described as ageworthy, cellar-worthy, long-lived, or similar, extend the sequence to at least 60 years or until the scores drop below 5.

Do not invent new wineVintageId values and omit any prose outside the JSON object.
""";

    public string TasteProfileGenerationSystemPrompt => TasteProfileSystemPromptText;

    public string SurfEyeSystemPrompt => SurfEyeSystemPromptText;

    public string SipSessionFoodSuggestionSystemPrompt => SipSessionFoodSuggestionSystemPromptText;

    public string WineWavesSystemPrompt => WineWavesSystemPromptText;

    public string DrinkingWindowSystemPrompt => DrinkingWindowSystemPromptText;

    public string BuildTasteProfilePrompt(
        IReadOnlyList<(Bottle Bottle, TastingNote Note)> scoredBottles,
        decimal? suggestionBudget)
    {
        if (scoredBottles is null || scoredBottles.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Create a cohesive wine taste profile for the user based on the scored bottles listed below.");
        builder.AppendLine("For the red light sections, do not just list characteristics that are generally considered bad for wine (even if the user mentions them), but instead highlight the specific characteristics that might otherwise be considered desirable.");
        builder.AppendLine("Each entry follows: Name (Vintage) — Origin | Attributes | Score | Notes.");
        builder.AppendLine();

        for (var index = 0; index < scoredBottles.Count; index++)
        {
            var (bottle, note) = scoredBottles[index];
            var wineVintage = bottle?.WineVintage;
            var wine = wineVintage?.Wine;

            var displayName = BuildWineDisplayName(wineVintage, wine);
            var origin = BuildWineOrigin(wine);
            var attributes = BuildWineAttributes(wine);
            var score = note.Score!.Value.ToString("0.##", CultureInfo.InvariantCulture);
            var noteText = PrepareNoteText(note.Note);

            builder.Append(index + 1);
            builder.Append(". ");
            builder.Append(displayName);

            if (!string.IsNullOrEmpty(origin))
            {
                builder.Append(" — ");
                builder.Append(origin);
            }

            if (!string.IsNullOrEmpty(attributes))
            {
                builder.Append(" | ");
                builder.Append(attributes);
            }

            builder.Append(" | Score: ");
            builder.Append(score);

            if (!string.IsNullOrEmpty(noteText))
            {
                builder.Append(" | Notes: ");
                builder.Append(noteText);
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Identify consistent stylistic preferences, texture, structure, and favored regions or grapes.");
        builder.AppendLine("Use only the provided information and avoid recommending specific new bottles.");
        builder.AppendLine("Also include exactly two suggested appellations or sub-appellations that match the palate, providing country, region, appellation, an optional subAppellation (use null when unknown), and a single-sentence reason under 200 characters explaining the fit. Suggest only appellations that are not already in use.");
        builder.AppendLine("For each suggested appellation list two or three representative wines from that location, giving the label name (without the vintage, grape varieties, regions, or appellation names), color (Red, White, or Rose), an optional variety, an optional subAppellation (e.g. the Burgundy village), and a vintage string that is either a four-digit year or \"NV\".");
        if (suggestionBudget.HasValue && suggestionBudget.Value > 0)
        {
            var budgetText = suggestionBudget.Value.ToString("0.##", CultureInfo.InvariantCulture);
            builder.AppendLine($"Ensure all suggested wines are priced at or below the user's suggestion budget of {budgetText} per bottle.");
        }
        builder.AppendLine("Respond only with JSON: {\"summary\":\"...\",\"profile\":\"...\",\"suggestedAppellations\":[{\"country\":\"...\",\"region\":\"...\",\"appellation\":\"...\",\"subAppellation\":null,\"reason\":\"...\",\"wines\":[{\"name\":\"...\",\"color\":\"Red\",\"variety\":\"...\",\"subAppellation\":null,\"vintage\":\"2019\"}]}]}. No markdown or commentary.");

        return builder.ToString();
    }

    public string BuildSurfEyePrompt(string? tasteProfileSummary, string tasteProfile)
    {
        var builder = new StringBuilder();
        //builder.AppendLine("Analyze the attached photo of wine bottles.");

        if (!string.IsNullOrWhiteSpace(tasteProfileSummary))
        {
            builder.Append("User taste profile summary: ");
            builder.AppendLine(tasteProfileSummary.Trim());
        }

        builder.Append("User taste profile details: ");
        builder.AppendLine(tasteProfile.Trim());
        builder.AppendLine("Identify each distinct wine label that appears in the photo and return at most five wines.");
        builder.AppendLine("Prioritize wines that match the user's taste preferences and explain the ranking succinctly.");
        builder.AppendLine("When recording a wine's name, omit grape varieties and vintages—they belong in their dedicated fields.");

        return builder.ToString();
    }

    public string BuildSurfEyePromptWithoutTasteProfile()
    {
        var builder = new StringBuilder();
        //builder.AppendLine("Analyze the attached photo of wine bottles.");
        builder.AppendLine("The user does not have a taste profile yet.");
        builder.AppendLine("Identify each distinct wine label in the information and return at most five wines.");
        builder.AppendLine("Sort wines from the most confidently identified to the least confidently identified.");
        builder.AppendLine("Provide concise notes to help the user verify each label.");
        builder.AppendLine("Set alignmentScore to 0 and leave alignmentSummary empty for every wine.");
        builder.AppendLine("Confidence must reflect how certain you are about the label identification.");
        builder.AppendLine("When recording a wine's name, omit grape varieties and vintages—they belong in their dedicated fields.");

        return builder.ToString();
    }

    public string BuildSurfEyeIdentificationPrompt()
    {
        var builder = new StringBuilder();
        //builder.AppendLine("Analyze the attached photo of wine bottles.");
        builder.AppendLine("Identify each distinct wine label in the information and return at most five wines.");
        builder.AppendLine("Focus solely on identification—do not reference or rely on the user's taste profile.");
        builder.AppendLine("Provide concise notes to help the user verify each label.");
        builder.AppendLine("Set alignmentScore to 0 and leave alignmentSummary empty for every wine.");
        builder.AppendLine("Confidence must reflect how certain you are about the label identification.");
        builder.AppendLine("When recording a wine's name, omit grape varieties and vintages—they belong in their dedicated fields.");

        return builder.ToString();
    }

    public string BuildSipSessionFoodSuggestionPrompt(SipSession session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var bottles = session.Bottles ?? Array.Empty<SipSessionBottle>();
        if (bottles.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var sessionName = string.IsNullOrWhiteSpace(session.Name)
            ? "this sip session"
            : $"the \"{session.Name.Trim()}\" sip session";

        builder.Append("The following wines will be tasted during ");
        builder.Append(sessionName);
        builder.AppendLine(". Recommend three complementary food pairings that guests can enjoy together.");
        builder.AppendLine("Ensure at least one pairing is vegetarian and start that suggestion with \"(Vegetarian)\".");
        builder.AppendLine("Also provide a dedicated cheese course recommendation suited to the lineup.");

        if (!string.IsNullOrWhiteSpace(session.Description))
        {
            builder.Append("Session context: ");
            builder.AppendLine(session.Description.Trim());
            builder.AppendLine();
        }

        var index = 1;
        foreach (var link in bottles)
        {
            var bottle = link?.Bottle;
            var wineVintage = bottle?.WineVintage;
            var wine = wineVintage?.Wine;

            var displayName = BuildWineDisplayName(wineVintage, wine);
            var origin = BuildWineOrigin(wine);
            var attributes = BuildWineAttributes(wine);

            builder.Append(index);
            builder.Append(". ");
            builder.Append(displayName);

            var details = new List<string>();
            if (!string.IsNullOrEmpty(origin))
            {
                details.Add(origin);
            }

            if (!string.IsNullOrEmpty(attributes))
            {
                details.Add(attributes);
            }

            if (bottle?.TastingNotes is { Count: > 0 })
            {
                var highlights = bottle.TastingNotes
                    .Where(note => note is not null)
                    .Select(note => PrepareNoteText(note!.Note))
                    .Where(note => !string.IsNullOrWhiteSpace(note))
                    .Take(2)
                    .ToList();

                if (highlights.Count > 0)
                {
                    details.Add($"Notes: {string.Join(" / ", highlights)}");
                }
            }

            if (details.Count > 0)
            {
                builder.Append(" — ");
                builder.Append(string.Join(" | ", details));
            }

            builder.AppendLine();
            index++;
        }

        builder.AppendLine();
        builder.AppendLine("Suggest dishes that harmonize with the overall lineup and briefly explain why each works.");
        builder.AppendLine("Respond ONLY with JSON shaped as {\"suggestions\":[\"Suggestion 1\",\"Suggestion 2\",\"Suggestion 3\"],\"cheese\":\"Cheese course\"}.");

        return builder.ToString();
    }

    private static string BuildWineDisplayName(WineVintage? wineVintage, Wine? wine)
    {
        var name = wine?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Unknown wine";
        }
        else
        {
            name = name.Trim();
        }

        if (wineVintage is not null && wineVintage.Vintage > 0)
        {
            return $"{name} {wineVintage.Vintage}";
        }

        return name;
    }

    private static string BuildWineOrigin(Wine? wine)
    {
        if (wine?.SubAppellation is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddDistinctPart(parts, wine.SubAppellation.Name);
        var appellation = wine.SubAppellation.Appellation;
        if (appellation is not null)
        {
            AddDistinctPart(parts, appellation.Name);
            var region = appellation.Region;
            if (region is not null)
            {
                AddDistinctPart(parts, region.Name);
                AddDistinctPart(parts, region.Country?.Name);
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static string BuildWineAttributes(Wine? wine)
    {
        if (wine is null)
        {
            return string.Empty;
        }

        var attributes = new List<string>();
        if (!string.IsNullOrWhiteSpace(wine.GrapeVariety))
        {
            attributes.Add(wine.GrapeVariety.Trim());
        }

        var color = wine.Color switch
        {
            WineColor.Rose => "Rosé",
            WineColor.White => "White",
            WineColor.Red => "Red",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(color))
        {
            attributes.Add(color);
        }

        return attributes.Count == 0 ? string.Empty : string.Join(" • ", attributes);
    }

    private static string PrepareNoteText(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return string.Empty;
        }

        var normalized = note.ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Length > 240)
        {
            normalized = normalized[..Math.Min(240, normalized.Length)].TrimEnd();
            if (!normalized.EndsWith("…", StringComparison.Ordinal))
            {
                normalized = $"{normalized}…";
            }
        }

        return normalized;
    }

    private static void AddDistinctPart(List<string> parts, string? value)
    {
        if (parts is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (parts.Exists(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        parts.Add(trimmed);
    }

    public string BuildWineWavesPrompt(
        IReadOnlyList<WineWavesPromptItem> vintages,
        string? tasteProfileSummary,
        string? tasteProfile)
    {
        if (vintages is null || vintages.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var hasTasteProfile = !string.IsNullOrWhiteSpace(tasteProfile);

        builder.AppendLine();
        builder.AppendLine("User taste profile details:");
        builder.AppendLine(hasTasteProfile ? tasteProfile!.Trim() : "(none provided)");
        builder.AppendLine();
        builder.AppendLine("Wines:");
        builder.AppendLine();

        for (var index = 0; index < vintages.Count; index++)
        {
            var vintage = vintages[index];
            builder.Append(index + 1);
            builder.Append(". ");
            builder.Append(vintage.Label);

            if (vintage.Vintage > 0)
            {
                builder.Append(" (Vintage ");
                builder.Append(vintage.Vintage.ToString(CultureInfo.InvariantCulture));
                builder.Append(')');
            }

            if (!string.IsNullOrWhiteSpace(vintage.Origin))
            {
                builder.Append(" — ");
                builder.Append(vintage.Origin);
            }

            builder.AppendLine();

            // if (!string.IsNullOrWhiteSpace(vintage.Attributes))
            // {
            //     builder.Append("Attributes: ");
            //     builder.AppendLine(vintage.Attributes);
            // }

            // if (vintage.Highlights is { Count: > 0 })
            // {
            //     builder.Append("Tasting notes: ");
            //     builder.AppendLine(string.Join(" | ", vintage.Highlights));
            // }

            builder.Append("wineVintageId: ");
            builder.AppendLine(vintage.WineVintageId.ToString());
            builder.AppendLine();
        }




        return builder.ToString();
    }

    public string BuildDrinkingWindowPrompt(string tasteProfile, string wineDescription)
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(tasteProfile)
            ? "No taste profile is available."
            : tasteProfile.Trim();

        var normalizedWine = string.IsNullOrWhiteSpace(wineDescription)
            ? "Unknown wine."
            : wineDescription.Trim();

        var builder = new StringBuilder();
        builder.AppendLine("You are a expert sommellier. Help the user find the perfect drinking window for a wine based on the user's taste profile. Consult professional wine critics and wine tasting notes to determine the wine's aging profile.");
        builder.AppendLine();
        builder.AppendLine("This is the user's taste profile:");
        builder.AppendLine();
        builder.AppendLine(normalizedProfile);
        builder.AppendLine();
        builder.AppendLine("This is the wine:");
        builder.AppendLine();
        builder.AppendLine(normalizedWine);

        return builder.ToString();
    }
}
