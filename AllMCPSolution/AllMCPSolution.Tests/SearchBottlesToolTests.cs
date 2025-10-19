using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllMCPSolution.Bottles;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using Xunit;

namespace AllMCPSolution.Tests;

public class SearchBottlesToolTests
{
    [Fact]
    public async Task SplitsTokensAcrossAppellationNames()
    {
        var bottle = CreateBottle(
            wineName: "Clos des Ducs",
            appellation: "Côte de Nuits Villages",
            region: "Burgundy",
            country: "France",
            vintage: 2018);

        var tool = CreateTool(bottle);
        var result = await ExecuteAsync(tool, "Villages; Cote");

        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal(1, result["totalMatches"]!.GetValue<int>());

        var firstResult = GetFirstResult(result);
        Assert.Contains("appellation", firstResult["matchedFields"]!.AsArray().Select(n => n!.GetValue<string>()));

        var villagesMatch = GetTokenMatch(firstResult, "Villages");
        AssertFieldMatch(villagesMatch, "appellation", "substring");

        var coteMatch = GetTokenMatch(firstResult, "Cote");
        AssertFieldMatch(coteMatch, "appellation", "substring");
    }

    [Fact]
    public async Task SupportsMixedCountryAndAppellationQueries()
    {
        var bottle = CreateBottle(
            wineName: "Château Latour",
            appellation: "Pauillac",
            region: "Bordeaux",
            country: "France",
            vintage: 2015);

        var tool = CreateTool(bottle);
        var result = await ExecuteAsync(tool, "France Pauillac");

        var firstResult = GetFirstResult(result);
        AssertFieldMatch(GetTokenMatch(firstResult, "France"), "country", "substring");
        AssertFieldMatch(GetTokenMatch(firstResult, "Pauillac"), "appellation", "substring");
    }

    [Fact]
    public async Task MatchesScoreKeywordsAndValues()
    {
        var note = new TastingNote
        {
            Id = Guid.NewGuid(),
            Note = "Rich blackberry and spice.",
            Score = 96m
        };

        var bottle = CreateBottle(
            wineName: "Scored Wine",
            appellation: "Napa Valley",
            region: "California",
            country: "United States",
            vintage: 2019,
            note);

        var tool = CreateTool(bottle);
        var result = await ExecuteAsync(tool, "96 pts");
        var firstResult = GetFirstResult(result);

        AssertFieldMatch(GetTokenMatch(firstResult, "96"), "tastingNoteScore", "substring");
        AssertFieldMatch(GetTokenMatch(firstResult, "pts"), "tastingNoteScoreKeywords", "substring");
    }

    [Fact]
    public async Task ProducesSnippetsForEachTastingNoteToken()
    {
        var note = new TastingNote
        {
            Id = Guid.NewGuid(),
            Note = "Bright cherry and cedar aromas linger on the finish."
        };

        var bottle = CreateBottle(
            wineName: "Aromatic Wine",
            appellation: "Willamette Valley",
            region: "Oregon",
            country: "United States",
            vintage: 2020,
            note);

        var tool = CreateTool(bottle);
        var result = await ExecuteAsync(tool, "cedar cherry");
        var firstResult = GetFirstResult(result);

        var cedarMatch = GetTokenMatch(firstResult, "cedar");
        AssertFieldMatch(cedarMatch, "tastingNote", "substring");
        AssertSnippetContains(cedarMatch, "cedar");

        var cherryMatch = GetTokenMatch(firstResult, "cherry");
        AssertFieldMatch(cherryMatch, "tastingNote", "substring");
        AssertSnippetContains(cherryMatch, "cherry");
    }

    private static async Task<JsonObject> ExecuteAsync(SearchBottlesTool tool, string query)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["query"] = query
        };

        var result = await tool.ExecuteAsync(parameters);
        var json = JsonSerializer.Serialize(result);
        return JsonNode.Parse(json)!.AsObject();
    }

    private static JsonObject GetFirstResult(JsonObject root)
    {
        return root["results"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .First();
    }

    private static JsonObject GetTokenMatch(JsonObject resultItem, string token)
    {
        return resultItem["tokenMatches"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .First(obj => string.Equals(obj["token"]!.GetValue<string>(), token, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFieldMatch(JsonObject tokenMatch, string expectedField, string expectedType)
    {
        var fields = tokenMatch["fields"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .ToList();

        Assert.Contains(fields, field =>
            string.Equals(field["field"]!.GetValue<string>(), expectedField, StringComparison.OrdinalIgnoreCase)
            && string.Equals(field["matchType"]!.GetValue<string>(), expectedType, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSnippetContains(JsonObject tokenMatch, string expectedSubstring)
    {
        var snippets = tokenMatch["tastingNotes"]!
            .AsArray()
            .Select(node => node!.AsObject()["snippet"]!.GetValue<string>());

        Assert.Contains(snippets, snippet => snippet.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase));
    }

    private static SearchBottlesTool CreateTool(params Bottle[] bottles)
    {
        return new SearchBottlesTool(new FakeBottleRepository(bottles.ToList()));
    }

    private static Bottle CreateBottle(string wineName, string appellation, string region, string country, int vintage, params TastingNote[] notes)
    {
        var countryEntity = new Country { Id = Guid.NewGuid(), Name = country };
        var regionEntity = new Region { Id = Guid.NewGuid(), Name = region, Country = countryEntity, CountryId = countryEntity.Id };
        var appellationEntity = new Appellation { Id = Guid.NewGuid(), Name = appellation, Region = regionEntity, RegionId = regionEntity.Id };
        var wine = new Wine
        {
            Id = Guid.NewGuid(),
            Name = wineName,
            Appellation = appellationEntity,
            AppellationId = appellationEntity.Id,
            GrapeVariety = "Pinot Noir"
        };

        var wineVintage = new WineVintage
        {
            Id = Guid.NewGuid(),
            Wine = wine,
            WineId = wine.Id,
            Vintage = vintage
        };

        var bottle = new Bottle
        {
            Id = Guid.NewGuid(),
            WineVintage = wineVintage,
            WineVintageId = wineVintage.Id,
            TastingNotes = notes.ToList()
        };

        foreach (var note in bottle.TastingNotes)
        {
            note.Bottle = bottle;
            note.BottleId = bottle.Id;
        }

        return bottle;
    }

    private sealed class FakeBottleRepository : IBottleRepository
    {
        private readonly List<Bottle> _bottles;

        public FakeBottleRepository(List<Bottle> bottles)
        {
            _bottles = bottles;
        }

        public Task<List<Bottle>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_bottles);
        public Task<Bottle?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Bottle bottle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Bottle bottle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
