using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("update_user", "Updates an existing user's name, taste profile, or summary.")]
public sealed class UpdateUserTool : UserToolBase
{
    public UpdateUserTool(IUserRepository userRepository)
        : base(userRepository)
    {
    }

    public override string Name => "update_user";
    public override string Description => "Updates an existing user.";
    public override string Title => "Update User";
    protected override string InvokingMessage => "Updating user…";
    protected override string InvokedMessage => "User updated.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var id = ParameterHelpers.GetGuidParameter(parameters, "id", "id");
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var newName = ParameterHelpers.GetStringParameter(parameters, "newName", "new_name")?.Trim();
        var newTasteProfile = ParameterHelpers.GetStringParameter(parameters, "newTasteProfile", "new_taste_profile")?.Trim();
        var newTasteProfileSummary = ParameterHelpers.GetStringParameter(parameters, "newTasteProfileSummary", "new_taste_profile_summary")?.Trim();

        var errors = new List<string>();
        if (id is null && string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Either 'id' or 'name' must be provided to locate the user.");
        }

        if (string.IsNullOrWhiteSpace(newName) && string.IsNullOrWhiteSpace(newTasteProfile) && string.IsNullOrWhiteSpace(newTasteProfileSummary))
        {
            errors.Add("At least one of 'newName', 'newTasteProfile', or 'newTasteProfileSummary' must be provided.");
        }

        if (errors.Count > 0)
        {
            return Failure("update", "Validation failed.", errors);
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

            return Failure("update",
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

        var shouldUpdateName = !string.IsNullOrWhiteSpace(newName)
            && !string.Equals(user.Name, newName, StringComparison.OrdinalIgnoreCase);

        if (shouldUpdateName)
        {
            var duplicate = await UserRepository.FindByNameAsync(newName!, ct);
            if (duplicate is not null && duplicate.Id != user.Id)
            {
                return Failure("update",
                    $"User '{newName}' already exists.",
                    new[] { $"User '{newName}' already exists." },
                    new
                    {
                        type = "user_exists",
                        user = UserResponseMapper.MapUser(duplicate)
                    });
            }

            user.Name = newName!;
        }

        var hasProfileChange = !string.IsNullOrWhiteSpace(newTasteProfile);
        var hasSummaryChange = !string.IsNullOrWhiteSpace(newTasteProfileSummary);

        if (shouldUpdateName)
        {
            await UserRepository.UpdateAsync(user, ct);
        }

        if (hasProfileChange || hasSummaryChange)
        {
            var currentProfile = user.ActiveTasteProfile?.Profile;
            var currentSummary = user.ActiveTasteProfile?.Summary;
            var profileValue = hasProfileChange ? newTasteProfile! : currentProfile ?? string.Empty;
            var summaryValue = hasSummaryChange ? newTasteProfileSummary : currentSummary;

            var updatedProfileUser = await UserRepository.UpdateTasteProfileAsync(user.Id, null, profileValue, summaryValue, ct);
            if (updatedProfileUser is not null)
            {
                user = updatedProfileUser;
            }
        }

        if (!shouldUpdateName && !(hasProfileChange || hasSummaryChange))
        {
            return Success("update", "No changes applied.", UserResponseMapper.MapUser(user));
        }

        var updated = await UserRepository.GetByIdAsync(user.Id, ct) ?? user;
        return Success("update", "User updated.", UserResponseMapper.MapUser(updated));
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
                    ["description"] = "Identifier of the user to update."
                },
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the user to update when id is not provided."
                },
                ["newName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "New name for the user."
                },
                ["newTasteProfile"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "New taste profile for the user."
                },
                ["newTasteProfileSummary"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "New short summary of the user's palate."
                }
            },
            ["required"] = new JsonArray()
        };
    }
}
