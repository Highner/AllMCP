using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("update_tasting_note", "Updates the content, score, or associations of an existing tasting note.")]
public sealed class UpdateTastingNoteTool : TastingNoteToolBase
{
    public UpdateTastingNoteTool(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository)
        : base(tastingNoteRepository, bottleRepository, userRepository)
    {
    }

    public override string Name => "update_tasting_note";
    public override string Description => "Updates an existing tasting note.";
    public override string Title => "Update Tasting Note";
    protected override string InvokingMessage => "Updating tasting note…";
    protected override string InvokedMessage => "Tasting note updated.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var note = ParameterHelpers.GetStringParameter(parameters, "note", "note")?.Trim();
        var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
        var userId = ParameterHelpers.GetGuidParameter(parameters, "userId", "user_id");
        var userName = ParameterHelpers.GetStringParameter(parameters, "userName", "user_name")?.Trim();
        var bottleId = ParameterHelpers.GetGuidParameter(parameters, "bottleId", "bottle_id");

        var hasNoteParameter = HasParameter(parameters, "note", "note");
        var hasScoreParameter = HasParameter(parameters, "score", "score");
        var hasUserParameter = HasParameter(parameters, "userId", "user_id") || HasParameter(parameters, "userName", "user_name");
        var hasBottleParameter = HasParameter(parameters, "bottleId", "bottle_id");

        var errors = new List<string>();
        if (id is null)
        {
            errors.Add("'id' is required to update a tasting note.");
        }

        if (!hasNoteParameter && !hasScoreParameter && !hasUserParameter && !hasBottleParameter)
        {
            errors.Add("Provide at least one field to update: note, score, user, or bottle.");
        }

        if (hasNoteParameter && string.IsNullOrWhiteSpace(note))
        {
            errors.Add("'note' cannot be empty when provided.");
        }

        if (hasScoreParameter)
        {
            if (score is null)
            {
                errors.Add("'score' must be a number between 0 and 100 when provided.");
            }
            else if (score < 0 || score > 100)
            {
                errors.Add("'score' must be between 0 and 100.");
            }
        }

        if (hasUserParameter && userId is null && string.IsNullOrWhiteSpace(userName))
        {
            errors.Add("When updating the user you must provide either 'userId' or 'userName'.");
        }

        if (errors.Count > 0)
        {
            return Failure("update", "Validation failed.", errors);
        }

        var existing = await TastingNoteRepository.GetByIdAsync(id!.Value, ct);
        if (existing is null)
        {
            return Failure("update",
                "Tasting note not found.",
                new[] { $"Tasting note with id '{id}' was not found." });
        }

        if (hasNoteParameter)
        {
            existing.Note = note!;
        }

        if (hasScoreParameter && score is not null)
        {
            existing.Score = score;
        }

        if (hasBottleParameter && bottleId is not null)
        {
            var bottle = await BottleRepository.GetByIdAsync(bottleId.Value, ct);
            if (bottle is null)
            {
                return Failure("update",
                    "Bottle not found.",
                    new[] { $"Bottle with id '{bottleId}' was not found." },
                    new
                    {
                        type = "bottle_not_found",
                        bottleId
                    });
            }

            existing.BottleId = bottle.Id;
            existing.Bottle = bottle;
        }

        if (hasUserParameter)
        {
            ApplicationUser? user = null;
            if (userId is not null)
            {
                user = await UserRepository.GetByIdAsync(userId.Value, ct);
            }

            if (user is null && !string.IsNullOrWhiteSpace(userName))
            {
                user = await UserRepository.FindByNameAsync(userName!, ct);
            }

            if (user is null)
            {
                var suggestions = string.IsNullOrWhiteSpace(userName)
                    ? Array.Empty<object>()
                    : (await UserRepository.SearchByApproximateNameAsync(userName!, 5, ct))
                        .Select(UserResponseMapper.MapUser)
                        .ToArray();

                return Failure("update",
                    "User not found.",
                    new[]
                    {
                        userId is not null
                            ? $"User with id '{userId}' was not found."
                            : $"User '{userName}' was not found."
                    },
                    new
                    {
                        type = "user_search",
                        query = userName ?? userId?.ToString(),
                        suggestions
                    });
            }

            existing.UserId = user.Id;
            existing.User = user;
        }

        await TastingNoteRepository.UpdateAsync(existing, ct);
        var updated = await TastingNoteRepository.GetByIdAsync(existing.Id, ct) ?? existing;

        return Success("update", "Tasting note updated.", TastingNoteResponseMapper.MapTastingNote(updated));
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the tasting note to update."
                },
                ["note"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Updated tasting note text."
                },
                ["score"] = new JsonObject
                {
                    ["type"] = "number",
                    ["description"] = "Updated score between 0 and 100."
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the new tasting note author."
                },
                ["userName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the new tasting note author if the id is unknown."
                },
                ["bottleId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the bottle the note should be attached to."
                }
            },
            ["required"] = new JsonArray("id")
        };
    }

    private static bool HasParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        if (parameters is null)
        {
            return false;
        }

        return parameters.ContainsKey(camelCase) || parameters.ContainsKey(snakeCase);
    }
}
