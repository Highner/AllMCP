using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Utilities;
using ExcelDataReader;

namespace AllMCPSolution.Services;

public interface IWineImportService
{
    Task<WineImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default);
    Task<WineImportResult> ImportBottlesAsync(
        Stream stream,
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<WineImportPreviewResult> PreviewBottleImportAsync(
        Stream stream,
        CancellationToken cancellationToken = default);
}

public sealed class WineImportService : IWineImportService
{
    private static readonly string[] WineRequiredColumns =
    [
        "Name",
        "Country",
        "Region",
        "Color",
        "Appellation",
        "SubAppellation"
    ];

    private static readonly string[] BottleRequiredColumns =
    [
        "Name",
        "Country",
        "Region",
        "Color",
        "Appellation",
        "SubAppellation",
        "Amount"
    ];

    private readonly ICountryRepository _countryRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IAppellationRepository _appellationRepository;
    private readonly ISubAppellationRepository _subAppellationRepository;
    private readonly IWineRepository _wineRepository;
    private readonly IWineVintageRepository _wineVintageRepository;
    private readonly IBottleRepository _bottleRepository;

    static WineImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public WineImportService(
        ICountryRepository countryRepository,
        IRegionRepository regionRepository,
        IAppellationRepository appellationRepository,
        ISubAppellationRepository subAppellationRepository,
        IWineRepository wineRepository,
        IWineVintageRepository wineVintageRepository,
        IBottleRepository bottleRepository)
    {
        _countryRepository = countryRepository;
        _regionRepository = regionRepository;
        _appellationRepository = appellationRepository;
        _subAppellationRepository = subAppellationRepository;
        _wineRepository = wineRepository;
        _wineVintageRepository = wineVintageRepository;
        _bottleRepository = bottleRepository;
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
        EnsureRequiredColumns(headerMap, WineRequiredColumns);

        var result = new WineImportResult();
        var cache = new ImportCache();
        var rowNumber = 1;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var row = ExtractRow(reader, headerMap, includeAmount: false);
            if (row is null || row.IsEmpty)
            {
                continue;
            }

            result.TotalRows++;

            var missingField = GetMissingRequiredField(row, requireAmount: false);
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

    public async Task<WineImportResult> ImportBottlesAsync(
        Stream stream,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier is required when importing bottles.", nameof(userId));
        }

        if (!stream.CanRead)
        {
            throw new InvalidDataException("The provided stream cannot be read.");
        }

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var headerMap = ReadHeader(reader);
        EnsureRequiredColumns(headerMap, BottleRequiredColumns);

        var result = new WineImportResult();
        var cache = new ImportCache();
        var rowNumber = 1;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var row = ExtractRow(reader, headerMap, includeAmount: true);
            if (row is null || row.IsEmpty)
            {
                continue;
            }

            result.TotalRows++;

            var missingField = GetMissingRequiredField(row, requireAmount: true);
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

            if (!TryParseAmount(row.Amount, out var amount, out var amountError))
            {
                result.RowErrors.Add(new WineImportRowError(rowNumber, amountError));
                continue;
            }

            try
            {
                await ProcessBottleRowAsync(row, color, amount, userId, result, cache, cancellationToken);
                result.ImportedRows++;
                result.AddedBottles += amount;
            }
            catch (Exception ex)
            {
                result.RowErrors.Add(new WineImportRowError(rowNumber, ex.Message));
            }
        }

        return result;
    }

    public async Task<WineImportPreviewResult> PreviewBottleImportAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new InvalidDataException("The provided stream cannot be read.");
        }

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var headerMap = ReadHeader(reader);
        EnsureRequiredColumns(headerMap, BottleRequiredColumns);

        var preview = new WineImportPreviewResult();
        var rowNumber = 1;
        var wineExistenceCache = new Dictionary<string, bool>();
        var countryCache = new Dictionary<string, Country?>(StringComparer.OrdinalIgnoreCase);
        var regionCache = new Dictionary<string, Region?>(StringComparer.OrdinalIgnoreCase);
        var appellationCache = new Dictionary<string, bool>();

        static string NormalizeKey(string? value) => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

        static string CreateKey(params string?[] parts)
        {
            const char separator = '\u001F';
            return string.Join(separator, parts.Select(NormalizeKey));
        }

        async Task<Country?> GetCountryAsync(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var key = NormalizeKey(name);
            if (countryCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var country = await _countryRepository.FindByNameAsync(name, cancellationToken);
            countryCache[key] = country;
            return country;
        }

        async Task<(bool Exists, Region? Region)> GetRegionAsync(string? countryName, string regionName)
        {
            var key = CreateKey(countryName, regionName);
            if (regionCache.TryGetValue(key, out var cachedRegion))
            {
                return (cachedRegion is not null, cachedRegion);
            }

            Region? region = null;

            if (!string.IsNullOrWhiteSpace(regionName))
            {
                if (!string.IsNullOrWhiteSpace(countryName))
                {
                    var country = await GetCountryAsync(countryName);
                    if (country is not null)
                    {
                        region = await _regionRepository.FindByNameAndCountryAsync(
                            regionName,
                            country.Id,
                            cancellationToken);
                    }
                }

                region ??= await _regionRepository.FindByNameAsync(regionName, cancellationToken);
            }

            regionCache[key] = region;
            return (region is not null, region);
        }

        async Task<bool> GetAppellationExistsAsync(Region? region, string appellationName)
        {
            if (region is null || string.IsNullOrWhiteSpace(appellationName))
            {
                return false;
            }

            var key = CreateKey(region.Id.ToString(), appellationName);
            if (appellationCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var appellation = await _appellationRepository.FindByNameAndRegionAsync(
                appellationName,
                region.Id,
                cancellationToken);
            var exists = appellation is not null;
            appellationCache[key] = exists;
            return exists;
        }

        async Task<bool> GetWineExistsAsync(WineImportRow row)
        {
            var key = CreateKey(row.Name, row.SubAppellation, row.Appellation);
            if (wineExistenceCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                wineExistenceCache[key] = false;
                return false;
            }

            const double maxNameDistance = 0.2d;
            const double maxHierarchyDistance = 0.15d;

            bool MatchesImportRow(WineImportRow source, Wine candidate)
            {
                var nameDistance = FuzzyMatchUtilities.CalculateNormalizedDistance(source.Name, candidate.Name);
                if (nameDistance > maxNameDistance)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(source.SubAppellation))
                {
                    var candidateSub = candidate.SubAppellation?.Name;
                    if (string.IsNullOrWhiteSpace(candidateSub))
                    {
                        return false;
                    }

                    var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(source.SubAppellation, candidateSub);
                    return distance <= maxHierarchyDistance;
                }

                if (!string.IsNullOrWhiteSpace(source.Appellation))
                {
                    var candidateAppellation = candidate.SubAppellation?.Appellation?.Name;
                    if (string.IsNullOrWhiteSpace(candidateAppellation))
                    {
                        return false;
                    }

                    var distance = FuzzyMatchUtilities.CalculateNormalizedDistance(source.Appellation, candidateAppellation);
                    return distance <= maxHierarchyDistance;
                }

                return true;
            }

            var matches = await _wineRepository.FindClosestMatchesAsync(
                row.Name!,
                maxResults: 5,
                cancellationToken);

            var exists = matches.Any(match => MatchesImportRow(row, match));
            wineExistenceCache[key] = exists;
            return exists;
        }

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var row = ExtractRow(reader, headerMap, includeAmount: true);
            if (row is null || row.IsEmpty)
            {
                continue;
            }

            preview.TotalRows++;

            var missingField = GetMissingRequiredField(row, requireAmount: true);
            if (missingField is not null)
            {
                preview.RowErrors.Add(new WineImportRowError(rowNumber, $"{missingField} is required."));
                continue;
            }

            if (!TryParseColor(row.Color, out var color, out var colorError))
            {
                preview.RowErrors.Add(new WineImportRowError(rowNumber, colorError));
                continue;
            }

            if (!TryParseAmount(row.Amount, out var amount, out var amountError))
            {
                preview.RowErrors.Add(new WineImportRowError(rowNumber, amountError));
                continue;
            }

            var wineExists = await GetWineExistsAsync(row);
            var country = await GetCountryAsync(row.Country);
            var countryExists = country is not null;
            var (regionExists, region) = await GetRegionAsync(row.Country, row.Region ?? string.Empty);
            var appellationExists = await GetAppellationExistsAsync(region, row.Appellation ?? string.Empty);

            preview.Rows.Add(new WineImportPreviewRow
            {
                RowNumber = rowNumber,
                Name = row.Name ?? string.Empty,
                Country = row.Country ?? string.Empty,
                Region = row.Region ?? string.Empty,
                Appellation = row.Appellation ?? string.Empty,
                SubAppellation = row.SubAppellation ?? string.Empty,
                Color = color.ToString(),
                Amount = amount,
                WineExists = wineExists,
                CountryExists = countryExists,
                RegionExists = regionExists,
                AppellationExists = appellationExists
            });
        }

        return preview;
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

    private static void EnsureRequiredColumns(
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyCollection<string> requiredColumns)
    {
        var missing = requiredColumns
            .Where(column => !headerMap.ContainsKey(column))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException($"The Excel file is missing the following required columns: {string.Join(", ", missing)}.");
        }
    }

    private static WineImportRow? ExtractRow(
        IExcelDataReader reader,
        IReadOnlyDictionary<string, int> headerMap,
        bool includeAmount)
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
            Normalize(GetValue("SubAppellation")),
            includeAmount ? Normalize(GetValue("Amount")) : null);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetMissingRequiredField(WineImportRow row, bool requireAmount)
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

        if (requireAmount && string.IsNullOrWhiteSpace(row.Amount))
        {
            return "Amount";
        }

        return null;
    }

    private static bool TryParseAmount(string? rawValue, out int amount, out string error)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            amount = 0;
            error = "Amount is required.";
            return false;
        }

        if (!int.TryParse(rawValue, out amount))
        {
            error = $"Amount '{rawValue}' is not a valid whole number.";
            return false;
        }

        if (amount <= 0)
        {
            error = "Amount must be greater than zero.";
            return false;
        }

        if (amount > 240)
        {
            error = "Amount must be 240 bottles or fewer.";
            return false;
        }

        error = string.Empty;
        return true;
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
        await GetOrCreateWineHierarchyAsync(row, color, result, cache, cancellationToken);
    }

    private async Task ProcessBottleRowAsync(
        WineImportRow row,
        WineColor color,
        int amount,
        Guid userId,
        WineImportResult result,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wine = await GetOrCreateWineHierarchyAsync(row, color, result, cache, cancellationToken);
        var wineVintage = await GetOrCreateWineVintageAsync(wine, cache, cancellationToken);

        for (var i = 0; i < amount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bottle = new Bottle
            {
                Id = Guid.NewGuid(),
                WineVintageId = wineVintage.Id,
                IsDrunk = false,
                DrunkAt = null,
                Price = null,
                BottleLocationId = null,
                UserId = userId
            };

            await _bottleRepository.AddAsync(bottle, cancellationToken);
        }
    }

    private async Task<Wine> GetOrCreateWineHierarchyAsync(
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
        return await GetOrCreateWineAsync(row.Name!, color, appellation, subAppellation, result, cache, cancellationToken);
    }

    private async Task<WineVintage> GetOrCreateWineVintageAsync(
        Wine wine,
        ImportCache cache,
        CancellationToken cancellationToken)
    {
        var cacheKey = wine.Id.ToString("D");
        if (cache.WineVintages.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var existing = await _wineVintageRepository.FindByWineAndVintageAsync(wine.Id, 0, cancellationToken)
            ?? await _wineVintageRepository.GetOrCreateAsync(wine.Id, 0, cancellationToken);

        cache.WineVintages[cacheKey] = existing;
        return existing;
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
        string? SubAppellation,
        string? Amount)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name)
            && string.IsNullOrWhiteSpace(Country)
            && string.IsNullOrWhiteSpace(Region)
            && string.IsNullOrWhiteSpace(Color)
            && string.IsNullOrWhiteSpace(Appellation)
            && string.IsNullOrWhiteSpace(SubAppellation)
            && string.IsNullOrWhiteSpace(Amount);
    }

    private sealed class ImportCache
    {
        public Dictionary<string, Country> Countries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Region> Regions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Appellation> Appellations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SubAppellation> SubAppellations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Wine> Wines { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WineVintage> WineVintages { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    public int AddedBottles { get; set; }
    public List<WineImportRowError> RowErrors { get; } = [];
    public bool HasRowErrors => RowErrors.Count > 0;
}

public sealed record WineImportRowError(int RowNumber, string Message);

public sealed class WineImportPreviewResult
{
    public int TotalRows { get; set; }
    public List<WineImportPreviewRow> Rows { get; } = [];
    public List<WineImportRowError> RowErrors { get; } = [];
}

public sealed class WineImportPreviewRow
{
    public int RowNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Appellation { get; set; } = string.Empty;
    public string SubAppellation { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int Amount { get; set; }
    public bool WineExists { get; set; }
    public bool CountryExists { get; set; }
    public bool RegionExists { get; set; }
    public bool AppellationExists { get; set; }
}
