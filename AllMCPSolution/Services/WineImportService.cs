using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using ExcelDataReader;

namespace AllMCPSolution.Services;

public interface IWineImportService
{
    Task<WineImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default);
}

public sealed class WineImportService : IWineImportService
{
    private static readonly string[] RequiredColumns =
    [
        "Name",
        "Country",
        "Region",
        "Color",
        "Appellation",
        "SubAppellation"
    ];

    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IWineRepository _wineRepository;

    static WineImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public WineImportService(
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        IWineRepository wineRepository)
    {
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _wineRepository = wineRepository;
    }

    public async Task<WineImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new InvalidDataException("The provided stream cannot be read.");
        }

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var headerMap = ReadHeader(reader);
        EnsureRequiredColumns(headerMap);

        var result = new WineImportResult();
        var cache = new ImportCache();
        var rowNumber = 1;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var row = ExtractRow(reader, headerMap);
            if (row is null || row.IsEmpty)
            {
                continue;
            }

            result.TotalRows++;

            var missingField = GetMissingRequiredField(row);
            if (missingField is not null)
            {
                result.RowErrors.Add(new WineImportRowError(rowNumber, $"{missingField} is required."));
                continue;
            }

            if (!TryParseColor(row.Color, out var color, out var colorError))
            {
                result.RowErrors.Add(new WineImportRowError(rowNumber, colorError));
                continue;
            }

            try
            {
                await ProcessRowAsync(row, color, result, cache, cancellationToken);
                result.ImportedRows++;
            }
            catch (Exception ex)
            {
                result.RowErrors.Add(new WineImportRowError(rowNumber, ex.Message));
            }
        }

        return result;
    }

    private static Dictionary<string, int> ReadHeader(IExcelDataReader reader)
    {
        if (!reader.Read())
        {
            throw new InvalidDataException("The Excel file does not contain any rows.");
        }

        var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.GetValue(i)?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            header[value.Trim()] = i;
        }

        return header;
    }

    private static void EnsureRequiredColumns(IReadOnlyDictionary<string, int> headerMap)
    {
        var missing = RequiredColumns
            .Where(column => !headerMap.ContainsKey(column))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException($"The Excel file is missing the following required columns: {string.Join(", ", missing)}.");
        }
    }

    private static WineImportRow? ExtractRow(IExcelDataReader reader, IReadOnlyDictionary<string, int> headerMap)
    {
        string? GetValue(string column) => headerMap.TryGetValue(column, out var index)
            ? reader.GetValue(index)?.ToString()
            : null;

        return new WineImportRow(
            Normalize(GetValue("Name")),
            Normalize(GetValue("Country")),
            Normalize(GetValue("Region")),
            Normalize(GetValue("Color")),
            Normalize(GetValue("Appellation")),
            Normalize(GetValue("SubAppellation")));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetMissingRequiredField(WineImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
        {
            return "Name";
        }

        if (string.IsNullOrWhiteSpace(row.Country))
        {
            return "Country";
        }

        if (string.IsNullOrWhiteSpace(row.Region))
        {
            return "Region";
        }

        if (string.IsNullOrWhiteSpace(row.Appellation))
        {
            return "Appellation";
        }

        return null;
    }

    private static bool TryParseColor(string? rawValue, out WineColor color, out string error)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            color = default;
            error = "Color is required.";
            return false;
        }

        if (Enum.TryParse(rawValue.Trim(), true, out color))
        {
            error = string.Empty;
            return true;
        }

        color = default;
        error = $"The color '{rawValue}' is not recognized. Expected Red, White, or Rose.";
        return false;
    }

    private async Task ProcessRowAsync(
        WineImportRow row,
        WineColor color,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var country = await GetOrCreateCountryAsync(row.Country!, result, cache, cancellationToken);
        var region = await GetOrCreateRegionAsync(row.Region!, country, result, cache, cancellationToken);
        var appellation = await GetOrCreateAppellationAsync(row.Appellation!, region, result, cache, cancellationToken);
        var subAppellation = await GetOrCreateSubAppellationAsync(row.SubAppellation, appellation, result, cache, cancellationToken);
        await GetOrCreateWineAsync(row.Name!, color, appellation, subAppellation, result, cache, cancellationToken);
    }

    private async Task<Country> GetOrCreateCountryAsync(
        string name,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        if (cache.Countries.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var existing = await _countryRepository.FindByNameAsync(normalized, cancellationToken);
        if (existing is null)
        {
            existing = await _countryRepository.GetOrCreateAsync(normalized, cancellationToken);
            result.CreatedCountries++;
        }

        cache.Countries[normalized] = existing;
        return existing;
    }

    private async Task<Region> GetOrCreateRegionAsync(
        string name,
        Country country,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var cacheKey = $"{country.Id:D}|{normalized}";
        if (cache.Regions.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var existing = await _regionRepository.FindByNameAndCountryAsync(normalized, country.Id, cancellationToken);
        if (existing is null)
        {
            existing = await _regionRepository.GetOrCreateAsync(normalized, country, cancellationToken);
            result.CreatedRegions++;
        }

        cache.Regions[cacheKey] = existing;
        return existing;
    }

    private async Task<Appellation> GetOrCreateAppellationAsync(
        string name,
        Region region,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var cacheKey = $"{region.Id:D}|{normalized}";
        if (cache.Appellations.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var existing = await _appellationRepository.FindByNameAndRegionAsync(normalized, region.Id, cancellationToken);
        if (existing is null)
        {
            existing = await _appellationRepository.GetOrCreateAsync(normalized, region.Id, cancellationToken);
            result.CreatedAppellations++;
        }

        cache.Appellations[cacheKey] = existing;
        return existing;
    }

    private async Task<SubAppellation> GetOrCreateSubAppellationAsync(
        string? name,
        Appellation appellation,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        var cacheKey = $"{appellation.Id:D}|{normalized}";
        if (cache.SubAppellations.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        SubAppellation? existing;
        if (string.IsNullOrEmpty(normalized))
        {
            existing = await _subAppellationRepository.FindByNameAndAppellationAsync(string.Empty, appellation.Id, cancellationToken);
            if (existing is null)
            {
                existing = await _subAppellationRepository.GetOrCreateBlankAsync(appellation.Id, cancellationToken);
                result.CreatedSubAppellations++;
            }
        }
        else
        {
            existing = await _subAppellationRepository.FindByNameAndAppellationAsync(normalized, appellation.Id, cancellationToken);
            if (existing is null)
            {
                existing = await _subAppellationRepository.GetOrCreateAsync(normalized, appellation.Id, cancellationToken);
                result.CreatedSubAppellations++;
            }
        }

        cache.SubAppellations[cacheKey] = existing;
        return existing;
    }

    private async Task<Wine> GetOrCreateWineAsync(
        string name,
        WineColor color,
        Appellation appellation,
        SubAppellation subAppellation,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var cacheKey = $"{subAppellation.Id:D}|{normalized}";
        if (cache.Wines.TryGetValue(cacheKey, out var cached))
        {
            if (cached.Color != color || cached.SubAppellationId != subAppellation.Id)
            {
                cached.Color = color;
                cached.SubAppellationId = subAppellation.Id;
                cached.SubAppellation = subAppellation;
                await _wineRepository.UpdateAsync(cached, cancellationToken);
                result.UpdatedWines++;
            }

            return cached;
        }

        var existing = await _wineRepository.FindByNameAsync(normalized, subAppellation.Name, appellation.Name, cancellationToken);
        if (existing is null || existing.SubAppellationId != subAppellation.Id)
        {
            var entity = new Wine
            {
                Id = Guid.NewGuid(),
                Name = normalized,
                Color = color,
                SubAppellationId = subAppellation.Id,
                SubAppellation = subAppellation
            };

            await _wineRepository.AddAsync(entity, cancellationToken);
            result.CreatedWines++;
            cache.Wines[cacheKey] = entity;
            return entity;
        }

        if (existing.Color != color || existing.SubAppellationId != subAppellation.Id)
        {
            existing.Color = color;
            existing.SubAppellationId = subAppellation.Id;
            existing.SubAppellation = subAppellation;
            await _wineRepository.UpdateAsync(existing, cancellationToken);
            result.UpdatedWines++;
        }

        cache.Wines[cacheKey] = existing;
        return existing;
    }

    private sealed record WineImportRow(
        string? Name,
        string? Country,
        string? Region,
        string? Color,
        string? Appellation,
        string? SubAppellation)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name)
            && string.IsNullOrWhiteSpace(Country)
            && string.IsNullOrWhiteSpace(Region)
            && string.IsNullOrWhiteSpace(Color)
            && string.IsNullOrWhiteSpace(Appellation)
            && string.IsNullOrWhiteSpace(SubAppellation);
    }

    private sealed class ImportCache
    {
        public Dictionary<string, Country> Countries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Region> Regions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Appellation> Appellations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SubAppellation> SubAppellations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Wine> Wines { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class WineImportResult
{
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int CreatedCountries { get; set; }
    public int CreatedRegions { get; set; }
    public int CreatedAppellations { get; set; }
    public int CreatedSubAppellations { get; set; }
    public int CreatedWines { get; set; }
    public int UpdatedWines { get; set; }
    public List<WineImportRowError> RowErrors { get; } = [];
    public bool HasRowErrors => RowErrors.Count > 0;
}

public sealed record WineImportRowError(int RowNumber, string Message);
