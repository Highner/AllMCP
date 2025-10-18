using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_tasting_note", "Creates a new tasting note for a specific bottle and user.")]
public sealed class CreateTastingNoteTool : TastingNoteToolBase
{
    public CreateTastingNoteTool(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository)
        : base(tastingNoteRepository, bottleRepository, userRepository)
    {
    }

    public override string Name => "create_tasting_note";
    public override string Description => "Creates a new tasting note.";
    public override string Title => "Create Tasting Note";
    protected override string InvokingMessage => "Recording tasting note…";
    protected override string InvokedMessage => "Tasting note created.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var note = ParameterHelpers.GetStringParameter(parameters, "note", "note")?.Trim();
        var score = ParameterHelpers.GetDecimalParameter(parameters, "score", "score");
        var userId = ParameterHelpers.GetGuidParameter(parameters, "userId", "user_id");
        var userName = ParameterHelpers.GetStringParameter(parameters, "userName", "user_name")?.Trim();
        var bottleId = ParameterHelpers.GetGuidParameter(parameters, "bottleId", "bottle_id");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(note))
        {
            errors.Add("'note' is required.");
        }

        if (bottleId is null)
        {
            errors.Add("'bottleId' is required.");
        }

        if (userId is null && string.IsNullOrWhiteSpace(userName))
        {
            errors.Add("Either 'userId' or 'userName' must be provided.");
        }

        if (score is not null && (score < 0 || score > 100))
        {
            errors.Add("'score' must be between 0 and 100.");
        }

        if (errors.Count > 0)
        {
            return Failure("create", "Validation failed.", errors);
        }

        var bottle = await BottleRepository.GetByIdAsync(bottleId!.Value, ct);
        if (bottle is null)
        {
            return Failure("create",
                "Bottle not found.",
                new[] { $"Bottle with id '{bottleId}' was not found." },
                new
                {
                    type = "bottle_not_found",
                    bottleId
                });
        }

        User? user = null;
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

            return Failure("create",
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

        var entity = new TastingNote
        {
            Id = Guid.NewGuid(),
            Note = note!,
            Score = score,
            BottleId = bottle.Id,
            UserId = user.Id,
            Bottle = bottle,
            User = user
        };

        await TastingNoteRepository.AddAsync(entity, ct);
        var persisted = await TastingNoteRepository.GetByIdAsync(entity.Id, ct) ?? entity;

        return Success("create", "Tasting note created.", TastingNoteResponseMapper.MapTastingNote(persisted));
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["note"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Full tasting note text."
                },
                ["score"] = new JsonObject
                {
                    ["type"] = "number",
                    ["description"] = "Optional score between 0 and 100."
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the note author."
                },
                ["userName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the note author when the id is unknown."
                },
                ["bottleId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                    ["description"] = "Identifier of the bottle the note belongs to."
                }
            },
            ["required"] = new JsonArray("note", "bottleId")
        };
    }
}
