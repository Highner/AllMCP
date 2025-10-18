using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_tasting_note", "Creates a new tasting note for a specific bottle and user.")]
public sealed class CreateTastingNoteTool : TastingNoteToolBase
{
    private readonly InventoryIntakeService _inventoryIntakeService;

    public CreateTastingNoteTool(
        ITastingNoteRepository tastingNoteRepository,
        IBottleRepository bottleRepository,
        IUserRepository userRepository,
        InventoryIntakeService inventoryIntakeService)
        : base(tastingNoteRepository, bottleRepository, userRepository)
    {
        _inventoryIntakeService = inventoryIntakeService;
    }

    public override string Name => "create_tasting_note";
    public override string Description => "Creates a new tasting note.";
    public override string Title => "Create Tasting Note";
    protected override string InvokingMessage => "Recording tasting note…";
    protected override string InvokedMessage => "Tasting note created.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var normalized = CreateCaseInsensitiveDictionary(parameters);
        var result = await _inventoryIntakeService.CreateTastingNoteAsync(normalized, ct);

        if (result.Success)
        {
            return Success("create", result.Message, result.TastingNote is null ? null : TastingNoteResponseMapper.MapTastingNote(result.TastingNote));
        }

        return Failure("create", result.Message, result.Errors, result.Suggestions, result.Exception);
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

    private static Dictionary<string, object?> CreateCaseInsensitiveDictionary(Dictionary<string, object>? parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return dict;
        }

        foreach (var kvp in parameters)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return dict;
    }
}
