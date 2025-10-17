using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("read_bottle", "Retrieves bottle information by id or lists all bottles.")]
public sealed class ReadBottleTool : BottleToolBase
{
    public ReadBottleTool(IBottleRepository bottleRepository)
        : base(bottleRepository)
    {
    }

    public override string Name => "read_bottle";
    public override string Description => "Retrieves bottle information by id or lists all bottles.";
    public override string Title => "Read Bottle";
    protected override string InvokingMessage => "Retrieving bottle data…";
    protected override string InvokedMessage => "Bottle data ready.";

    protected override async Task<BottleOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");

        if (id is null)
        {
            var bottles = await BottleRepository.GetAllAsync(ct);
            var mapped = bottles.Select(BottleResponseMapper.MapBottle).ToList();
            return Success("read", $"Retrieved {mapped.Count} bottles.", new
            {
                count = mapped.Count,
                bottles = mapped
            });
        }

        var bottle = await BottleRepository.GetByIdAsync(id.Value, ct);
        if (bottle is null)
        {
            return Failure("read", $"Bottle with id {id} was not found.", new[] { $"Bottle with id {id} was not found." });
        }

        return Success("read", "Bottle retrieved successfully.", new
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
                    ["description"] = "Bottle identifier. When omitted all bottles are returned."
                }
            }
        };
    }
}
