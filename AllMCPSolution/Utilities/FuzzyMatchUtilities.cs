using System;
using System.Collections.Generic;
using System.Linq;

namespace AllMCPSolution.Utilities;

public static class FuzzyMatchUtilities
{
    public static int CalculateLevenshteinDistance(string source, string target)
    {
        source ??= string.Empty;
        target ??= string.Empty;

        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }

    public static IReadOnlyList<T> FindClosestMatches<T>(
        IEnumerable<T> items,
        string query,
        Func<T, string> selector,
        int maxResults = 5,
        double maxNormalizedDistance = 0.45)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        if (maxResults <= 0) return Array.Empty<T>();
        if (double.IsNaN(maxNormalizedDistance) || maxNormalizedDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNormalizedDistance));
        }

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length == 0)
        {
            return items
                .Select(item => new { item, value = selector(item) ?? string.Empty })
                .OrderBy(x => x.value, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(x => x.item)
                .ToList();
        }

        var loweredQuery = normalizedQuery.ToLowerInvariant();

        return items
            .Select(item =>
            {
                var value = selector(item) ?? string.Empty;
                var normalizedValue = value.Trim();
                var loweredValue = normalizedValue.ToLowerInvariant();
                var contains = loweredValue.Contains(loweredQuery, StringComparison.Ordinal);
                var distance = CalculateLevenshteinDistance(loweredQuery, loweredValue);
                var maxLength = Math.Max(loweredValue.Length, loweredQuery.Length);
                var normalizedDistance = maxLength == 0 ? 0 : (double)distance / maxLength;
                return new
                {
                    item,
                    contains,
                    distance,
                    normalizedDistance,
                    length = normalizedValue.Length,
                    original = value
                };
            })
            .Where(x => x.contains || x.normalizedDistance <= maxNormalizedDistance)
            .OrderBy(x => x.contains ? 0 : 1)
            .ThenBy(x => x.distance)
            .ThenBy(x => x.length)
            .ThenBy(x => x.original, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(x => x.item)
            .ToList();
    }
}
