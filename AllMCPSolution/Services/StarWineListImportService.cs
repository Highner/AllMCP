using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AllMCPSolution.Services;

public interface IStarWineListImportService
{
    Task<StarWineListImportResult> ParseAsync(
        Stream htmlStream,
        CancellationToken cancellationToken = default);
}

public sealed class StarWineListImportService : IStarWineListImportService
{
    private static readonly Regex ListItemRegex = new(
        "<li[^>]*class=\\\"[^\\\"]*producers-page__list-item[^\\\"]*\\\"[^>]*>(.*?)</li>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(
        "<a[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SpanRegex = new(
        "<span[^>]*>(.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RegionRegex = new(
        "<h1[^>]*class=\\\"[^\\\"]*producers-page__title[^\\\"]*\\\"[^>]*>\\s*Wine producers\\s*-\\s*(?<region>[^<]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CountryBreadcrumbRegex = new(
        "<a[^>]*href=\\\"[^\\\"]*/wine-producers/(?<slug>[^\\\"#?]+)\\\"[^>]*class=\\\"[^\\\"]*crumbs-common__breadcrumbs-item[^\\\"]*\\\"[^>]*>(?<name>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<StarWineListImportResult> ParseAsync(
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
            return new StarWineListImportResult(Array.Empty<StarWineListProducer>(), null, null);
        }

        var results = new List<StarWineListProducer>();

        foreach (Match match in ListItemRegex.Matches(content))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inner = match.Groups[1].Value;
            var nameMatch = LinkRegex.Match(inner);
            if (!nameMatch.Success)
            {
                continue;
            }

            var rawName = StripTags(nameMatch.Groups[1].Value);
            var name = WebUtility.HtmlDecode(rawName).Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var spanMatch = SpanRegex.Match(inner);
            var rawAppellation = spanMatch.Success ? StripTags(spanMatch.Groups[1].Value) : string.Empty;
            var appellation = WebUtility.HtmlDecode(rawAppellation).Trim();

            results.Add(new StarWineListProducer(name, appellation));
        }

        var region = ExtractRegion(content);
        var country = ExtractCountry(content);

        return new StarWineListImportResult(results, country, region);
    }

    private static string StripTags(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
    }

    private static string? ExtractRegion(string content)
    {
        var match = RegionRegex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["region"].Value;
        var decoded = WebUtility.HtmlDecode(value).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string? ExtractCountry(string content)
    {
        foreach (Match match in CountryBreadcrumbRegex.Matches(content))
        {
            if (!match.Success)
            {
                continue;
            }

            var slug = match.Groups["slug"].Value;
            if (string.IsNullOrWhiteSpace(slug) || slug.Contains('/'))
            {
                continue;
            }

            var rawName = StripTags(match.Groups["name"].Value);
            var decoded = WebUtility.HtmlDecode(rawName).Trim();
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        return null;
    }
}

public sealed record StarWineListProducer(string Name, string Appellation);
public sealed record StarWineListImportResult(
    IReadOnlyList<StarWineListProducer> Producers,
    string? Country,
    string? Region);
