using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;

namespace AllMCPSolution.Services;

public interface IInflationService
{
    Task<decimal> GetAdjustmentFactorAsync(DateTime sourceDate, DateTime? targetDate = null, CancellationToken ct = default);
    Task<decimal> AdjustAmountAsync(decimal amount, DateTime sourceDate, DateTime? targetDate = null, CancellationToken ct = default);
}

/// <summary>
/// Service to fetch and cache monthly HICP index data from the European Central Bank
/// and provide inflation adjustment factors between two dates.
/// </summary>
public class EcbInflationService : IInflationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EcbInflationService> _logger;
    private readonly IInflationIndexRepository _repo;

    // Cache of monthly index values: key "yyyy-MM" -> index value
    private readonly ConcurrentDictionary<string, decimal> _monthlyIndex = new();
    private DateTime _lastFetchUtc = DateTime.MinValue;

    // Default series: HICP All-items, Euro area (U2), Monthly index (2015=100)
    // SDW series key approximation: ICP.M.U2.N.000000.4.INX
    private readonly string _seriesKey;

    public EcbInflationService(IHttpClientFactory httpClientFactory, ILogger<EcbInflationService> logger, IConfiguration config, IInflationIndexRepository repo)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(EcbInflationService));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AllMCPSolution", "1.0"));
        _logger = logger;
        _repo = repo;
        _seriesKey = config.GetValue<string>("Inflation:EcbSeriesKey") ?? "ICP.M.U2.N.000000.4.INX";
    }

    public async Task<decimal> AdjustAmountAsync(decimal amount, DateTime sourceDate, DateTime? targetDate = null, CancellationToken ct = default)
    {
        var factor = await GetAdjustmentFactorAsync(sourceDate, targetDate, ct);
        return decimal.Round(amount * factor, 2);
    }


    public async Task<decimal> GetAdjustmentFactorAsync(DateTime sourceDate, DateTime? targetDate = null, CancellationToken ct = default)
    {
        targetDate ??= DateTime.UtcNow.Date;
        try
        {
            await EnsureIndexDataAsync(ct);

            var srcKey = KeyFor(sourceDate);
            var tgtKey = KeyFor(targetDate.Value);

            // Try to get source index, or find closest available
            if (!_monthlyIndex.TryGetValue(srcKey, out var srcIndex))
            {
                srcIndex = FindClosestIndex(sourceDate);
                if (srcIndex <= 0)
                {
                    _logger.LogWarning("No inflation data available for or near source date {SourceDate}", sourceDate);
                    return 1m;
                }
            }

            // Try to get target index, or find closest available (most recent)
            if (!_monthlyIndex.TryGetValue(tgtKey, out var tgtIndex))
            {
                tgtIndex = FindClosestIndex(targetDate.Value);
                if (tgtIndex <= 0)
                {
                    _logger.LogWarning("No inflation data available for or near target date {TargetDate}", targetDate.Value);
                    return 1m;
                }
            }

            if (srcIndex <= 0 || tgtIndex <= 0) return 1m;
            return tgtIndex / srcIndex;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute inflation adjustment factor.");
        }
        // Graceful fallback if data unavailable
        return 1m;
    }

    private decimal FindClosestIndex(DateTime targetDate)
    {
        if (_monthlyIndex.IsEmpty) return 0m;

        // Start from target month and go backwards to find the most recent available data
        var searchDate = new DateTime(targetDate.Year, targetDate.Month, 1);
        
        // First, try up to 12 months back from target
        for (int i = 0; i < 12; i++)
        {
            var key = searchDate.ToString("yyyy-MM");
            if (_monthlyIndex.TryGetValue(key, out var index))
            {
                if (i > 0)
                {
                    _logger.LogInformation("Using inflation index from {Month} for requested date {TargetDate}", 
                        searchDate.ToString("yyyy-MM"), targetDate.ToString("yyyy-MM"));
                }
                return index;
            }
            searchDate = searchDate.AddMonths(-1);
        }

        // If nothing found in last 12 months, get the most recent available data point
        var latestKey = _monthlyIndex.Keys
            .OrderByDescending(k => k)
            .FirstOrDefault();

        if (latestKey != null && _monthlyIndex.TryGetValue(latestKey, out var latestIndex))
        {
            _logger.LogInformation("Using latest available inflation index from {Month} for requested date {TargetDate}", 
                latestKey, targetDate.ToString("yyyy-MM"));
            return latestIndex;
        }

        return 0m;
    }

    private static string KeyFor(DateTime date) => new DateTime(date.Year, date.Month, 1).ToString("yyyy-MM");

        // Parses ECB CSV into a dictionary keyed by month start (UTC)
        private Dictionary<DateTime, decimal> ParseCsvToDict(string csv)
        {
            var result = new Dictionary<DateTime, decimal>();
            using var reader = new StringReader(csv);
            var header = reader.ReadLine();
            if (header == null) return result;

            var headers = header.Split(',');
            var timeIdx = Array.FindIndex(headers, h => h.Equals("TIME_PERIOD", StringComparison.OrdinalIgnoreCase));
            var valIdx = Array.FindIndex(headers, h => h.Equals("OBS_VALUE", StringComparison.OrdinalIgnoreCase));
            if (timeIdx < 0 || valIdx < 0)
            {
                timeIdx = 0; valIdx = 1;
            }

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsvLine(line);
                if (parts.Length <= Math.Max(timeIdx, valIdx)) continue;

                var time = parts[timeIdx].Trim('"');
                var valueStr = parts[valIdx].Trim('"');

                if (!DateTime.TryParseExact(time, new[] { "yyyy-MM", "yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                if (decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var idxVal))
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1);
                    result[monthStart] = idxVal;
                }
            }
            return result;
        }
    private async Task EnsureIndexDataAsync(CancellationToken ct)
    {
        // Determine the last fully finished month (UTC)
        var lastCompleteMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1);

        // 1) Check DB latest
        var dbLatestPeriod = await _repo.GetLatestFinishedMonthAsync(ct);

        // 2) If cache empty or stale, hydrate from DB first
        if (_monthlyIndex.IsEmpty || (DateTime.UtcNow - _lastFetchUtc) > TimeSpan.FromHours(12))
        {
            var all = await _repo.GetAllAsync(ct);
            foreach (var row in all)
            {
                _monthlyIndex[new DateTime(row.Year, row.Month, 1).ToString("yyyy-MM")] = row.IndexValue;
            }
            _lastFetchUtc = DateTime.UtcNow;
        }

        // 3) If DB is up to date through lastCompleteMonth, nothing to do
        if (dbLatestPeriod.HasValue && dbLatestPeriod.Value >= lastCompleteMonth)
        {
            return;
        }

        // Otherwise, fetch from ECB and persist missing months
        // Split "ICP.M.U2.N.000000.4.INX" into flowRef="ICP" and key="M.U2.N.000000.4.INX"
        var firstDot = _seriesKey.IndexOf('.');
        string flowRef, key;
        if (firstDot > 0)
        {
            flowRef = _seriesKey.Substring(0, firstDot);
            key = _seriesKey.Substring(firstDot + 1);
        }
        else
        {
            // fallback: whole thing is a flowRef, key empty (unlikely for your use case)
            flowRef = _seriesKey;
            key = string.Empty;
        }

        // Use the new ECB API entry point; ask for CSV data only
        var url =
            $"https://data-api.ecb.europa.eu/service/data/{flowRef}/{key}?lastNObservations=600&detail=dataonly&format=csvdata";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("text/csv");
            using var resp = await _httpClient.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var csv = await resp.Content.ReadAsStringAsync(ct);
            var parsed = ParseCsvToDict(csv);

            // Build list of new/updated rows from (dbLatestPeriod exclusive) through lastCompleteMonth
            var itemsToUpsert = new List<InflationIndex>();
            DateTime start = dbLatestPeriod?.AddMonths(1) ?? parsed.Keys.Min();
            // Ensure start isn't earlier than available data
            if (parsed.Count > 0)
            {
                var minAvailable = parsed.Keys.Min();
                if (start < minAvailable) start = minAvailable;
            }
            for (var d = start; d <= lastCompleteMonth; d = d.AddMonths(1))
            {
                var keyStr = d.ToString("yyyy-MM");
                if (parsed.TryGetValue(d, out var val))
                {
                    itemsToUpsert.Add(new InflationIndex
                    {
                        Year = d.Year,
                        Month = d.Month,
                        IndexValue = val
                    });
                    _monthlyIndex[keyStr] = val; // update cache
                }
            }

            if (itemsToUpsert.Count > 0)
            {
                await _repo.UpsertRangeAsync(itemsToUpsert, ct);
            }

            _lastFetchUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ECB inflation data from {Url}", url);
        }
    }


    private void ParseCsv(string csv)
    {
        // Expecting lines with TIME_PERIOD,OBS_VALUE (CSV with header). We will look for those columns.
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header == null) return;

        var headers = header.Split(',');
        var timeIdx = Array.FindIndex(headers, h => h.Equals("TIME_PERIOD", StringComparison.OrdinalIgnoreCase));
        var valIdx = Array.FindIndex(headers, h => h.Equals("OBS_VALUE", StringComparison.OrdinalIgnoreCase));

        if (timeIdx < 0 || valIdx < 0)
        {
            // Try SDMX-CSV alternative: first two columns may be TIME_PERIOD;OBS_VALUE
            timeIdx = 0; valIdx = 1;
        }

        string? line;
        var newIndex = new ConcurrentDictionary<string, decimal>();
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = SplitCsvLine(line);
            if (parts.Length <= Math.Max(timeIdx, valIdx)) continue;

            var time = parts[timeIdx].Trim('"');
            var valueStr = parts[valIdx].Trim('"');

            if (!DateTime.TryParseExact(time, new[] { "yyyy-MM", "yyyy" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                continue;

            if (decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var idxVal))
            {
                newIndex[KeyFor(date)] = idxVal;
            }
        }

        if (newIndex.Count > 0)
        {
            foreach (var kv in newIndex)
                _monthlyIndex[kv.Key] = kv.Value;
        }
    }

    private static string[] SplitCsvLine(string line)
    {
        // Very simple CSV split handling quotes; adequate for ECB format here
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(ch);
            }
        }
        result.Add(cur.ToString());
        return result.ToArray();
    }
}
