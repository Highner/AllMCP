using System.Linq;
using AllMCPSolution.Models;

namespace AllMCPSolution.Tools;

internal static class BottleResponseMapper
{
    public static object MapBottle(Bottle bottle)
    {
        if (bottle is null)
        {
            return new { };
        }

        var wineVintage = bottle.WineVintage;
        var wine = wineVintage?.Wine;
        var tastingNotes = bottle.TastingNotes?.OrderBy(note => note.Id).ToList();
        var latestNote = tastingNotes?.LastOrDefault();
        return new
        {
            id = bottle.Id,
            price = bottle.Price,
            score = latestNote?.Score,
            tastingNote = latestNote?.Note,
            tastingNotes = tastingNotes is null || tastingNotes.Count == 0
                ? null
                : tastingNotes.Select(note => new
                {
                    id = note.Id,
                    tastingNote = note.Note,
                    score = note.Score,
                    userId = note.UserId
                }).ToList(),
            vintage = wineVintage?.Vintage,
            isDrunk = bottle.IsDrunk,
            drunkAt = bottle.DrunkAt,
            wineVintageId = bottle.WineVintageId,
            wineId = wine?.Id,
            wine = wine is null ? null : MapWine(wine)
        };
    }

    public static object MapWineSummary(Wine wine)
        => MapWine(wine);

    public static object MapCountry(Country country)
        => new { id = country.Id, name = country.Name };

    public static object MapRegion(Region region)
        => new
        {
            id = region.Id,
            name = region.Name,
            country = region.Country is null
                ? null
                : new { id = region.Country.Id, name = region.Country.Name }
        };

    private static object MapWine(Wine wine)
    {
        var subAppellation = wine.SubAppellation;
        var appellation = subAppellation?.Appellation;
        var region = appellation?.Region;
        var country = region?.Country;

        return new
        {
            id = wine.Id,
            name = wine.Name,
            grapeVariety = wine.GrapeVariety,
            subAppellationId = subAppellation?.Id,
            subAppellation = string.IsNullOrWhiteSpace(subAppellation?.Name) ? null : subAppellation.Name,
            appellationId = appellation?.Id,
            appellation = string.IsNullOrWhiteSpace(appellation?.Name) ? null : appellation.Name,
            color = wine.Color.ToString(),
            country = country is null ? null : new { id = country.Id, name = country.Name },
            region = region is null
                ? null
                : new
                {
                    id = region.Id,
                    name = region.Name,
                    country = country is null ? null : new { id = country.Id, name = country.Name }
                }
        };
    }
}
