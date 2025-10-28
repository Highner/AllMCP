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
    Task<IReadOnlyList<StarWineListProducer>> ExtractProducersAsync(
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

    public async Task<IReadOnlyList<StarWineListProducer>> ExtractProducersAsync(
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
            return Array.Empty<StarWineListProducer>();
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

        return results;
    }

    private static string StripTags(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
    }
}

public sealed record StarWineListProducer(string Name, string Appellation);
