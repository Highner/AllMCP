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

    private static readonly Regex QuantitySpanRegex = new(
        "<span[^>]*class=['\"][^'\"]*\\bel\\s+num[^'\"]*['\"][^>]*>(?<content>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FourDigitVintagePrefixRegex = new(
        @"^\s*(?<year>\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VintagePrefixRegex = new(
        @"^\s*(?<value>(?:N\.?V\.?|N/V|Non\s+Vintage|\d{2}))(?:\s+|(?=[^A-Za-z]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            var hasBottleDetails = hasQuantityInfo && vintage.HasValue;

            wines.Add(new CellarTrackerWine(cleanedName, appellation, variety, vintage, quantity, hasBottleDetails));
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
    bool HasBottleDetails);

public sealed record CellarTrackerImportResult(
    IReadOnlyList<CellarTrackerWine> Wines,
    string? Country,
    string? Region,
    string? Color);
