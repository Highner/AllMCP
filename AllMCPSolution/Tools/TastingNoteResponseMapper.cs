using AllMCPSolution.Models;

namespace AllMCPSolution.Tools;

internal static class TastingNoteResponseMapper
{
    public static object MapTastingNote(TastingNote tastingNote)
    {
        if (tastingNote is null)
        {
            return new { };
        }

        var bottle = tastingNote.Bottle;
        var wineVintage = bottle?.WineVintage;
        var wine = wineVintage?.Wine;
        var user = tastingNote.User;

        return new
        {
            id = tastingNote.Id,
            note = tastingNote.Note,
            score = tastingNote.Score,
            bottleId = tastingNote.BottleId,
            bottle = bottle is null
                ? null
                : new
                {
                    id = bottle.Id,
                    wineVintageId = bottle.WineVintageId,
                    wine = wine is null
                        ? null
                        : new
                        {
                            id = wine.Id,
                            name = wine.Name,
                            appellation = string.IsNullOrWhiteSpace(wine.Appellation?.Name)
                                ? null
                                : wine.Appellation!.Name,
                            color = wine.Color.ToString()
                        }
                },
            userId = tastingNote.UserId,
            user = user is null
                ? null
                : new
                {
                    id = user.Id,
                    name = user.Name,
                    tasteProfile = user.TasteProfile
                }
        };
    }
}
