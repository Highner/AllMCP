using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("read_user", "Retrieves an existing user by id or name.")]
public sealed class ReadUserTool : UserToolBase
{
    public ReadUserTool(IUserRepository userRepository)
        : base(userRepository)
    {
    }

    public override string Name => "read_user";
    public override string Description => "Retrieves an existing user.";
    public override string Title => "Read User";
    protected override string InvokingMessage => "Fetching user…";
    protected override string InvokedMessage => "User loaded.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("read",
                "Either 'id' or 'name' must be provided.",
                new[] { "Either 'id' or 'name' must be provided." });
        }

        ApplicationUser? user = null;
        if (id is not null)
        {
            user = await UserRepository.GetByIdAsync(id.Value, ct);
        }

        if (user is null && !string.IsNullOrWhiteSpace(name))
        {
            user = await UserRepository.FindByNameAsync(name!, ct);
        }

        if (user is null)
        {
            var suggestions = string.IsNullOrWhiteSpace(name)
                ? Array.Empty<object>()
                : (await UserRepository.SearchByApproximateNameAsync(name!, 5, ct))
                    .Select(UserResponseMapper.MapUser)
                    .ToArray();

            return Failure("read",
                "User not found.",
                new[]
                {
                    id is not null
                        ? $"User with id '{id}' was not found."
                        : $"User '{name}' was not found."
                },
                new
                {
                    type = "user_search",
                    query = name ?? id?.ToString(),
                    suggestions
                });
        }

        return Success("read", "User retrieved.", UserResponseMapper.MapUser(user));
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
                    ["description"] = "User identifier."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "User name."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
