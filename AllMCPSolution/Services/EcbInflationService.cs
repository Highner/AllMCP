using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;

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

    // Cache of monthly index values: key "yyyy-MM" -> index value
    private readonly ConcurrentDictionary<string, decimal> _monthlyIndex = new();
    private DateTime _lastFetchUtc = DateTime.MinValue;

    // Default series: HICP All-items, Euro area (U2), Monthly index (2015=100)
    // SDW series key approximation: ICP.M.U2.N.000000.4.INX
    private readonly string _seriesKey;

    public EcbInflationService(IHttpClientFactory httpClientFactory, ILogger<EcbInflationService> logger, IConfiguration config)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(EcbInflationService));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AllMCPSolution", "1.0"));
        _logger = logger;
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

            if (_monthlyIndex.TryGetValue(srcKey, out var srcIndex) && _monthlyIndex.TryGetValue(tgtKey, out var tgtIndex))
            {
                if (srcIndex <= 0 || tgtIndex <= 0) return 1m;
                return tgtIndex / srcIndex;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute inflation adjustment factor.");
        }
        // Graceful fallback if data unavailable
        return 1m;
    }

    private static string KeyFor(DateTime date) => new DateTime(date.Year, date.Month, 1).ToString("yyyy-MM");

    private async Task EnsureIndexDataAsync(CancellationToken ct)
    {
        // Refresh at most every 12 hours
        if ((DateTime.UtcNow - _lastFetchUtc) < TimeSpan.FromHours(12) && _monthlyIndex.Count > 0)
            return;

        var url = $"https://sdw-wsrest.ecb.europa.eu/service/data/{_seriesKey}?lastNObservations=600&format=csvdata";

        try
        {
            using var resp = await _httpClient.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var csv = await resp.Content.ReadAsStringAsync(ct);
            ParseCsv(csv);
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
