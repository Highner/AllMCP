using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("delete_bottle", "Deletes an existing bottle after confirming it exists.")]
public sealed class DeleteBottleTool : BottleToolBase
{
    public DeleteBottleTool(IBottleRepository bottleRepository)
        : base(bottleRepository)
    {
    }

    public override string Name => "delete_bottle";
    public override string Description => "Deletes an existing bottle after confirming it exists.";
    public override string Title => "Delete Bottle";
    protected override string InvokingMessage => "Deleting bottle…";
    protected override string InvokedMessage => "Bottle deleted.";

    protected override async Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        if (id is null)
        {
            return Failure("delete", "'id' is required.", new[] { "'id' is required." });
        }

        var bottle = await BottleRepository.GetByIdAsync(id.Value, ct);
        if (bottle is null)
        {
            return Failure("delete", $"Bottle with id {id} was not found.", new[] { $"Bottle with id {id} was not found." });
        }

        await BottleRepository.DeleteAsync(id.Value, ct);

        return Success("delete", $"Bottle '{id}' deleted successfully.", new
        {
            bottle = BottleResponseMapper.MapBottle(bottle)
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
                    ["description"] = "Identifier of the bottle to delete."
                }
            },
            ["required"] = new JsonArray("id")
        };
    }
}
