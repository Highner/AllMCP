using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("delete_user", "Deletes a user profile.")]
public sealed class DeleteUserTool : UserToolBase
{
    public DeleteUserTool(IUserRepository userRepository)
        : base(userRepository)
    {
    }

    public override string Name => "delete_user";
    public override string Description => "Deletes an existing user.";
    public override string Title => "Delete User";
    protected override string InvokingMessage => "Deleting user…";
    protected override string InvokedMessage => "User deleted.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();

        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            return Failure("delete",
                "Either 'id' or 'name' must be provided.",
                new[] { "Either 'id' or 'name' must be provided." });
        }

        User? user = null;
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

            return Failure("delete",
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

        await UserRepository.DeleteAsync(user.Id, ct);

        return Success("delete",
            "User deleted.",
            new
            {
                id = user.Id,
                name = user.Name,
                tasteProfile = user.TasteProfile
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
                    ["description"] = "Identifier of the user to delete."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the user to delete when id is not provided."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
