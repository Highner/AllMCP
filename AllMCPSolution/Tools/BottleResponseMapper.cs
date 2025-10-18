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

        var wine = bottle.Wine;
        return new
        {
            id = bottle.Id,
            price = bottle.Price,
            score = bottle.Score,
            tastingNote = bottle.TastingNote,
            vintage = bottle.Vintage,
            isDrunk = bottle.IsDrunk,
            drunkAt = bottle.DrunkAt,
            wineId = bottle.WineId,
            wine = wine is null
                ? null
                : new
                {
                    id = wine.Id,
                    name = wine.Name,
                    grapeVariety = wine.GrapeVariety,
                    color = wine.Color.ToString(),
                    country = wine.Country is null
                        ? null
                        : new { id = wine.Country.Id, name = wine.Country.Name },
                    region = wine.Region is null
                        ? null
                        : new { id = wine.Region.Id, name = wine.Region.Name }
                }
        };
    }

    public static object MapWineSummary(Wine wine)
    {
        return new
        {
            id = wine.Id,
            name = wine.Name,
            grapeVariety = wine.GrapeVariety,
            color = wine.Color.ToString(),
            country = wine.Country is null ? null : new { id = wine.Country.Id, name = wine.Country.Name },
            region = wine.Region is null ? null : new { id = wine.Region.Id, name = wine.Region.Name }
        };
    }

    public static object MapCountry(Country country)
        => new { id = country.Id, name = country.Name };

    public static object MapRegion(Region region)
        => new { id = region.Id, name = region.Name };
}
