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
        return new
        {
            id = bottle.Id,
            price = bottle.Price,
            score = bottle.Score,
            tastingNote = bottle.TastingNote,
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
        var appellation = wine.Appellation;
        var region = appellation?.Region;
        var country = region?.Country;

        return new
        {
            id = wine.Id,
            name = wine.Name,
            grapeVariety = wine.GrapeVariety,
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
