using System;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("read_tasting_note", "Retrieves a tasting note by its identifier.")]
public sealed class ReadTastingNoteTool : TastingNoteToolBase
{
    public ReadTastingNoteTool(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository)
        : base(tastingNoteRepository, bottleRepository, userRepository)
    {
    }

    public override string Name => "read_tasting_note";
    public override string Description => "Retrieves an existing tasting note.";
    public override string Title => "Read Tasting Note";
    protected override string InvokingMessage => "Loading tasting note…";
    protected override string InvokedMessage => "Tasting note loaded.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return Failure("read",
                "'id' is required to read a tasting note.",
                new[] { "'id' is required." });
        }

        var note = await TastingNoteRepository.GetByIdAsync(id.Value, ct);
        if (note is null)
        {
            return Failure("read",
                "Tasting note not found.",
                new[] { $"Tasting note with id '{id}' was not found." });
        }

        return Success("read", "Tasting note retrieved.", TastingNoteResponseMapper.MapTastingNote(note));
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
                    ["description"] = "Identifier of the tasting note to retrieve."
                }
            },
            ["required"] = new JsonArray("id")
        };
    }
}
