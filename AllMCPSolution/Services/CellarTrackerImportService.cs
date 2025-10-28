using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AllMCPSolution.Services;

public interface ICellarTrackerImportService
{
    Task<CellarTrackerImportResult> ParseAsync(
        Stream htmlStream,
        CancellationToken cancellationToken = default);
}

public sealed class CellarTrackerImportService : ICellarTrackerImportService
{
    private static readonly Regex HiddenInputRegex = new(
        "<input[^>]*type=['\"]hidden['\"][^>]*name=['\"](?<name>Region|Type|Country)['\"][^>]*value=['\"](?<value>[^'\"]*)['\"][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WineRowRegex = new(
        "<tr[^>]*>(?<content>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WineCellRegex = new(
        "<td[^>]*class=['\"][^'\"]*\\bname\\b[^'\"]*['\"][^>]*>(?<content>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(
        "<a[^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HeaderRegex = new(
        "<h3[^>]*>(?<content>.*?)</h3>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AppellationSpanRegex = new(
        "<span[^>]*class=['\"][^'\"]*el\\s+loc[^'\"]*['\"][^>]*>(?<content>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex VarietySpanRegex = new(
        "<span[^>]*class=['\"][^'\"]*el\\s+var[^'\"]*['\"][^>]*>(?<content>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex QuantitySpanRegex = CreateElSpanRegex("num");

    private static readonly Regex ConsumptionStatusSpanRegex = CreateElSpanRegex("con");

    private static readonly Regex ConsumptionDateSpanRegex = CreateElSpanRegex("dat");

    private static readonly Regex ConsumptionNoteSpanRegex = CreateElSpanRegex("not");

    private static readonly Regex AnchorWithHrefRegex = new(
        "<a[^>]*href=['\"](?<href>[^'\"]*)['\"][^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ScorePrefixRegex = new(
        @"^\s*(?<first>\d+(?:[.,]\d+)?)(?:\s*[-–]\s*(?<second>\d+(?:[.,]\d+)?))?",
        RegexOptions.Compiled);

    private static readonly Regex FourDigitVintagePrefixRegex = new(
        @"^\s*(?<year>\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VintagePrefixRegex = new(
        @"^\s*(?<value>(?:N\.?V\.?|N/V|Non\s+Vintage|\d{2}))(?:\s+|(?=[^A-Za-z]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex CreateElSpanRegex(string className)
    {
        var escaped = Regex.Escape(className);
        var pattern =
            $"<span[^>]*class=['\"](?>[^'\"]*?)(?=[^'\"]*\\bel\\b)(?=[^'\"]*\\b{escaped}\\b)[^'\"]*['\"][^>]*>(?<content>.*?)</span>";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    }

    public async Task<CellarTrackerImportResult> ParseAsync(
        Stream htmlStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(htmlStream);

        if (!htmlStream.CanRead)
        {
            throw new InvalidDataException("The provided stream cannot be read.");
        }

        if (htmlStream.CanSeek)
        {
            htmlStream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(htmlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new CellarTrackerImportResult(Array.Empty<CellarTrackerWine>(), null, null, null);
        }

        string? country = null;
        string? region = null;
        string? color = null;

        foreach (Match match in HiddenInputRegex.Matches(content))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = match.Groups["name"].Value;
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
            if (string.Equals(name, "Country", StringComparison.OrdinalIgnoreCase))
            {
                country ??= string.IsNullOrWhiteSpace(value) ? null : value;
            }
            else if (string.Equals(name, "Region", StringComparison.OrdinalIgnoreCase))
            {
                region ??= string.IsNullOrWhiteSpace(value) ? null : value;
            }
            else if (string.Equals(name, "Type", StringComparison.OrdinalIgnoreCase))
            {
                color ??= string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        var wines = new List<CellarTrackerWine>();

        foreach (Match rowMatch in WineRowRegex.Matches(content))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowContent = rowMatch.Groups["content"].Value;
            if (string.IsNullOrWhiteSpace(rowContent))
            {
                continue;
            }

            var cellMatch = WineCellRegex.Match(rowContent);
            if (!cellMatch.Success)
            {
                continue;
            }

            var inner = cellMatch.Groups["content"].Value;
            if (string.IsNullOrWhiteSpace(inner))
            {
                continue;
            }

            var headerMatch = HeaderRegex.Match(inner);
            var headerContent = headerMatch.Success ? headerMatch.Groups["content"].Value : inner;
            var nameMatch = LinkRegex.Match(headerContent);
            var rawName = nameMatch.Success ? StripTags(nameMatch.Groups["text"].Value) : StripTags(headerContent);
            var decodedName = WebUtility.HtmlDecode(rawName).Trim();

            var (cleanedName, vintage) = ExtractNameAndVintage(decodedName);
            if (string.IsNullOrWhiteSpace(cleanedName))
            {
                continue;
            }

            var appellationMatch = AppellationSpanRegex.Match(inner);
            var rawAppellation = appellationMatch.Success ? StripTags(appellationMatch.Groups["content"].Value) : string.Empty;
            var appellation = WebUtility.HtmlDecode(rawAppellation).Trim();

            if (string.IsNullOrWhiteSpace(appellation) && !string.IsNullOrWhiteSpace(region))
            {
                appellation = region;
            }

            var varietyMatch = VarietySpanRegex.Match(inner);
            var rawVariety = varietyMatch.Success ? StripTags(varietyMatch.Groups["content"].Value) : string.Empty;
            var variety = WebUtility.HtmlDecode(rawVariety).Trim();

            var (quantity, hasQuantityInfo) = ExtractQuantity(rowContent);
            var consumption = ExtractConsumption(inner, rowContent);

            if (consumption.IsConsumed && quantity <= 0)
            {
                quantity = 1;
            }

            var hasBottleDetails = (hasQuantityInfo || consumption.IsConsumed) && vintage.HasValue;

            wines.Add(new CellarTrackerWine(
                cleanedName,
                appellation,
                variety,
                vintage,
                quantity,
                hasBottleDetails,
                consumption.IsConsumed,
                consumption.Date,
                consumption.Score,
                consumption.Note));
        }

        return new CellarTrackerImportResult(wines, country, region, color);
    }

    private static string StripTags(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
    }

    private static (string CleanedName, int? Vintage) ExtractNameAndVintage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, null);
        }

        var trimmed = value.Trim();
        int? vintage = null;

        var fourDigitMatch = FourDigitVintagePrefixRegex.Match(trimmed);
        if (fourDigitMatch.Success && fourDigitMatch.Length > 0)
        {
            var token = fourDigitMatch.Groups["year"].Value;
            var maybeVintage = ParseVintageToken(token);
            if (maybeVintage.HasValue)
            {
                vintage = maybeVintage;
            }

            trimmed = trimmed[fourDigitMatch.Length..]
                .TrimStart(' ', '\u00A0', '-', '—', ',', '.', '\'', '\"');
        }

        while (true)
        {
            var match = VintagePrefixRegex.Match(trimmed);
            if (!match.Success || match.Length == 0)
            {
                break;
            }

            var token = match.Groups["value"].Value;
            if (!vintage.HasValue)
            {
                var maybeVintage = ParseVintageToken(token);
                if (maybeVintage.HasValue)
                {
                    vintage = maybeVintage;
                }
            }

            trimmed = trimmed[match.Length..]
                .TrimStart(' ', '\u00A0', '-', '—', ',', '.', '\'', '\"');
        }

        return (trimmed.Trim(), vintage);
    }

    private static (int Quantity, bool HasQuantityInfo) ExtractQuantity(string rowContent)
    {
        if (string.IsNullOrWhiteSpace(rowContent))
        {
            return (1, false);
        }

        var match = QuantitySpanRegex.Match(rowContent);
        if (!match.Success)
        {
            return (1, false);
        }

        var rawQuantity = StripTags(match.Groups["content"].Value);
        var decoded = WebUtility.HtmlDecode(rawQuantity).Trim();

        if (!string.IsNullOrEmpty(decoded))
        {
            var normalized = decoded.Replace('\u00A0', ' ').Trim();
            var segments = normalized.Split('+');
            if (segments.Length > 1)
            {
                var total = 0;
                var parsedAny = false;

                foreach (var segment in segments)
                {
                    var trimmedSegment = segment.Trim();
                    if (trimmedSegment.Length == 0)
                    {
                        continue;
                    }

                    if (!int.TryParse(trimmedSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var part) || part < 0)
                    {
                        parsedAny = false;
                        total = 0;
                        break;
                    }

                    total += part;
                    parsedAny = true;
                }

                if (parsedAny && total > 0)
                {
                    return (total, true);
                }
            }

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                return (parsed, true);
            }
        }

        return (1, false);
    }

    private static CellarTrackerConsumptionInfo ExtractConsumption(string cellContent, string rowContent)
    {
        var primary = ExtractConsumptionFromSource(cellContent);
        var fallback = ExtractConsumptionFromSource(rowContent);

        if (!HasConsumptionData(fallback))
        {
            return primary;
        }

        if (!HasConsumptionData(primary))
        {
            return fallback;
        }

        var isConsumed = primary.IsConsumed || fallback.IsConsumed;
        var consumptionDate = primary.Date ?? fallback.Date;
        var consumptionScore = primary.Score ?? fallback.Score;
        var consumptionNote = !string.IsNullOrWhiteSpace(primary.Note)
            ? primary.Note
            : fallback.Note;

        return new CellarTrackerConsumptionInfo(isConsumed, consumptionDate, consumptionScore, consumptionNote);
    }

    private static CellarTrackerConsumptionInfo ExtractConsumptionFromSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return CellarTrackerConsumptionInfo.Empty;
        }

        var isConsumed = false;
        DateTime? consumptionDate = null;
        decimal? consumptionScore = null;
        string? consumptionNote = null;

        var statusMatch = ConsumptionStatusSpanRegex.Match(source);
        if (statusMatch.Success)
        {
            var statusText = WebUtility.HtmlDecode(StripTags(statusMatch.Groups["content"].Value)).Trim();
            if (!string.IsNullOrWhiteSpace(statusText)
                && statusText.Contains("drank", StringComparison.OrdinalIgnoreCase))
            {
                isConsumed = true;
            }
        }

        var dateMatch = ConsumptionDateSpanRegex.Match(source);
        if (dateMatch.Success)
        {
            var dateContent = dateMatch.Groups["content"].Value;
            var (parsedDate, foundDate) = ExtractConsumptionDate(dateContent);
            if (foundDate)
            {
                consumptionDate = parsedDate;
                isConsumed = true;
            }
        }

        var noteMatch = ConsumptionNoteSpanRegex.Match(source);
        if (noteMatch.Success)
        {
            var rawNote = StripTags(noteMatch.Groups["content"].Value);
            var decoded = WebUtility.HtmlDecode(rawNote);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                var normalized = Regex.Replace(decoded, "\\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    var (score, note) = ExtractScoreAndNote(normalized);
                    if (score.HasValue)
                    {
                        consumptionScore = score;
                    }

                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        consumptionNote = note;
                    }

                    if (score.HasValue || !string.IsNullOrWhiteSpace(consumptionNote))
                    {
                        isConsumed = true;
                    }
                }
            }
        }

        return new CellarTrackerConsumptionInfo(isConsumed, consumptionDate, consumptionScore, consumptionNote);
    }

    private static bool HasConsumptionData(CellarTrackerConsumptionInfo info)
    {
        return info.IsConsumed
            || info.Date.HasValue
            || info.Score.HasValue
            || !string.IsNullOrWhiteSpace(info.Note);
    }

    private static (DateTime? Date, bool Found) ExtractConsumptionDate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, false);
        }

        var linkMatch = AnchorWithHrefRegex.Match(content);
        if (linkMatch.Success)
        {
            var href = WebUtility.HtmlDecode(linkMatch.Groups["href"].Value);
            if (!string.IsNullOrWhiteSpace(href))
            {
                var rawValue = ExtractQueryParameter(href, "ConsumedBegin");
                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    var decoded = Uri.UnescapeDataString(rawValue);
                    if (TryParseConsumptionDate(decoded, out var parsed))
                    {
                        return (NormalizeConsumptionDate(parsed), true);
                    }
                }
            }

            var linkText = WebUtility.HtmlDecode(StripTags(linkMatch.Groups["text"].Value));
            if (!string.IsNullOrWhiteSpace(linkText) && TryParseConsumptionDate(linkText, out var parsedText))
            {
                return (NormalizeConsumptionDate(parsedText), true);
            }
        }

        var fallback = WebUtility.HtmlDecode(StripTags(content));
        if (!string.IsNullOrWhiteSpace(fallback) && TryParseConsumptionDate(fallback, out var parsedFallback))
        {
            return (NormalizeConsumptionDate(parsedFallback), true);
        }

        return (null, false);
    }

    private static string? ExtractQueryParameter(string href, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        var questionIndex = href.IndexOf('?', StringComparison.Ordinal);
        if (questionIndex < 0 || questionIndex >= href.Length - 1)
        {
            return null;
        }

        var query = href[(questionIndex + 1)..];
        var segments = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var parts = segment.Split('=', 2, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                continue;
            }

            if (parts[0].Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return null;
    }

    private static bool TryParseConsumptionDate(string value, out DateTime result)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            result = default;
            return false;
        }

        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "MM/dd/yyyy",
            "dd/MM/yyyy"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(
                trimmed,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result))
            {
                return true;
            }
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        return false;
    }

    private static (decimal? Score, string Note) ExtractScoreAndNote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, string.Empty);
        }

        var match = ScorePrefixRegex.Match(value);
        if (!match.Success || match.Length == 0)
        {
            return (null, value.Trim());
        }

        var firstToken = match.Groups["first"].Value;
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return (null, value.Trim());
        }

        static decimal? ParseScore(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var normalized = token.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        var firstScore = ParseScore(firstToken);
        if (!firstScore.HasValue)
        {
            return (null, value.Trim());
        }

        decimal? finalScore = firstScore;
        var secondToken = match.Groups["second"].Value;
        if (!string.IsNullOrWhiteSpace(secondToken))
        {
            var secondScore = ParseScore(secondToken);
            if (secondScore.HasValue)
            {
                finalScore = Math.Round((firstScore.Value + secondScore.Value) / 2m, 2, MidpointRounding.AwayFromZero);
            }
        }

        var remainder = value[match.Length..];
        remainder = remainder.TrimStart(' ', '-', '–', ':', '.', ',', '\u2013', '\u2014');
        remainder = Regex.Replace(remainder, "\\s+", " ").Trim();

        return (finalScore, remainder);
    }

    private static DateTime NormalizeConsumptionDate(DateTime value)
    {
        var dateOnly = value.Date;
        return dateOnly >= DateTime.MaxValue.Date ? dateOnly : dateOnly.AddDays(1);
    }

    private static int? ParseVintageToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalized = token.Trim();
        if (normalized.Equals("NV", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("N.V", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("N/V", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Non Vintage", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            if (normalized.Length == 2)
            {
                var currentYearSuffix = DateTime.UtcNow.Year % 100;
                var century = parsed <= currentYearSuffix ? 2000 : 1900;
                return century + parsed;
            }

            return parsed;
        }

        return null;
    }
}

public sealed record CellarTrackerWine(
    string Name,
    string Appellation,
    string Variety,
    int? Vintage,
    int Quantity,
    bool HasBottleDetails,
    bool IsConsumed,
    DateTime? ConsumptionDate,
    decimal? ConsumptionScore,
    string? ConsumptionNote);

public sealed record CellarTrackerImportResult(
    IReadOnlyList<CellarTrackerWine> Wines,
    string? Country,
    string? Region,
    string? Color);

internal sealed record CellarTrackerConsumptionInfo(
    bool IsConsumed,
    DateTime? Date,
    decimal? Score,
    string? Note)
{
    public static CellarTrackerConsumptionInfo Empty { get; } = new(false, null, null, null);
}
