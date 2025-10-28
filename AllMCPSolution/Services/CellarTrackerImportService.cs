using System;
using System.Collections.Generic;
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

    private static readonly Regex LeadingVintageRegex = new(
        @"^\s*(?:N\.?V\.?|N/V|Non\s+Vintage|\d{2}|\d{4})(?:\s+|(?=[^A-Za-z]))",
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

        foreach (Match cellMatch in WineCellRegex.Matches(content))
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var cleanedName = RemoveVintagePrefix(decodedName);
            if (string.IsNullOrWhiteSpace(cleanedName))
            {
                continue;
            }

            var appellationMatch = AppellationSpanRegex.Match(inner);
            var rawAppellation = appellationMatch.Success ? StripTags(appellationMatch.Groups["content"].Value) : string.Empty;
            var appellation = WebUtility.HtmlDecode(rawAppellation).Trim();

            var varietyMatch = VarietySpanRegex.Match(inner);
            var rawVariety = varietyMatch.Success ? StripTags(varietyMatch.Groups["content"].Value) : string.Empty;
            var variety = WebUtility.HtmlDecode(rawVariety).Trim();

            wines.Add(new CellarTrackerWine(cleanedName, appellation, variety));
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

    private static string RemoveVintagePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        // Remove common vintage prefixes such as four-digit years or NV/NV.
        while (true)
        {
            var match = LeadingVintageRegex.Match(trimmed);
            if (!match.Success || match.Length == 0)
            {
                break;
            }

            trimmed = trimmed[match.Length..].TrimStart('-', '—', ',', '.', '\'');
        }

        return trimmed.Trim();
    }
}

public sealed record CellarTrackerWine(string Name, string Appellation, string Variety);

public sealed record CellarTrackerImportResult(
    IReadOnlyList<CellarTrackerWine> Wines,
    string? Country,
    string? Region,
    string? Color);
