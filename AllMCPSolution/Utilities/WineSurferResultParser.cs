using System;
using System.Collections.Generic;
using System.Text.Json;
using AllMCPSolution.Models;

namespace AllMCPSolution.Utilities;

public static class WineSurferResultParser
{
    private const int MaxResults = 6;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static bool TryParse(string? content, out IReadOnlyList<WineSurferLookupResult> matches)
    {
        matches = Array.Empty<WineSurferLookupResult>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var segment = ExtractJsonSegment(content);

        try
        {
            using var document = JsonDocument.Parse(segment, DocumentOptions);
            var root = document.RootElement;
            IEnumerable<JsonElement> elements;

            if (root.ValueKind == JsonValueKind.Array)
            {
                elements = root.EnumerateArray();
            }
            else if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("wines", out var winesElement)
                && winesElement.ValueKind == JsonValueKind.Array)
            {
                elements = winesElement.EnumerateArray();
            }
            else
            {
                return false;
            }

            var list = new List<WineSurferLookupResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = TryGetTrimmedString(element, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    continue;
                }

                var region = TryGetTrimmedString(element, "region");
                var appellation = TryGetTrimmedString(element, "appellation");
                var subAppellation = TryGetTrimmedString(element, "subAppellation")
                    ?? TryGetTrimmedString(element, "sub_appellation");

                list.Add(new WineSurferLookupResult(name, region, appellation, subAppellation));
                if (list.Count == MaxResults)
                {
                    break;
                }
            }

            matches = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractJsonSegment(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && closingFence > firstNewLine)
            {
                trimmed = trimmed.Substring(firstNewLine + 1, closingFence - firstNewLine - 1);
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed.Trim();
    }

    private static string? TryGetTrimmedString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                return property.GetRawText();
            }
        }

        return null;
    }
}
