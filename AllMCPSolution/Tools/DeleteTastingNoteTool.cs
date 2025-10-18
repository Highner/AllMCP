using System;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("delete_tasting_note", "Deletes an existing tasting note by id.")]
public sealed class DeleteTastingNoteTool : TastingNoteToolBase
{
    public DeleteTastingNoteTool(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository)
        : base(tastingNoteRepository, bottleRepository, userRepository)
    {
    }

    public override string Name => "delete_tasting_note";
    public override string Description => "Deletes a tasting note.";
    public override string Title => "Delete Tasting Note";
    protected override string InvokingMessage => "Deleting tasting note…";
    protected override string InvokedMessage => "Tasting note deleted.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return Failure("delete",
                "'id' is required to delete a tasting note.",
                new[] { "'id' is required." });
        }

        var existing = await TastingNoteRepository.GetByIdAsync(id.Value, ct);
        if (existing is null)
        {
            return Failure("delete",
                "Tasting note not found.",
                new[] { $"Tasting note with id '{id}' was not found." });
        }

        await TastingNoteRepository.DeleteAsync(id.Value, ct);
        return Success("delete", "Tasting note deleted.", new
        {
            id = id.Value
        });
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
                    ["description"] = "Identifier of the tasting note to delete."
                }
            },
            ["required"] = new JsonArray("id")
        };
    }
}
