using System.Text.Json.Nodes;
using AllMCPSolution.Attributes;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;

namespace AllMCPSolution.Tools;

[McpTool("create_user", "Creates a new user with a taste profile for personalized recommendations.")]
public sealed class CreateUserTool : UserToolBase
{
    public CreateUserTool(IUserRepository userRepository)
        : base(userRepository)
    {
    }

    public override string Name => "create_user";
    public override string Description => "Creates a new user entry.";
    public override string Title => "Create User";
    protected override string InvokingMessage => "Creating user…";
    protected override string InvokedMessage => "User created.";

    protected override async Task<CrudOperationResult> ExecuteInternalAsync(Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var name = ParameterHelpers.GetStringParameter(parameters, "name", "name")?.Trim();
        var tasteProfile = ParameterHelpers.GetStringParameter(parameters, "tasteProfile", "taste_profile")?.Trim();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("'name' is required.");
        }

        if (string.IsNullOrWhiteSpace(tasteProfile))
        {
            errors.Add("'tasteProfile' is required.");
        }

        if (errors.Count > 0)
        {
            return Failure("create", "Validation failed.", errors);
        }

        var existing = await UserRepository.FindByNameAsync(name!, ct);
        if (existing is not null)
        {
            return Failure("create",
                $"User '{existing.Name}' already exists.",
                new[] { $"User '{existing.Name}' already exists." },
                new
                {
                    type = "user_exists",
                    user = UserResponseMapper.MapUser(existing)
                });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name!,
            TasteProfile = tasteProfile!
        };

        await UserRepository.AddAsync(user, ct);
        var persisted = await UserRepository.GetByIdAsync(user.Id, ct) ?? user;
        return Success("create", "User created.", UserResponseMapper.MapUser(persisted));
    }

    protected override JsonObject BuildInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Name of the user to create."
                },
                ["tasteProfile"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Taste profile description for personalization."
                }
            },
            ["required"] = new JsonArray("name", "tasteProfile")
        };
    }
}
