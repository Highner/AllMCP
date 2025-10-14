

using System.Text.Json;

using AllMCPSolution.Data;

namespace AllMCPSolution.Services;

public static class ParameterHelpers
{
    /// <summary>
    /// Gets a parameter value supporting both camelCase and snake_case naming conventions
    /// </summary>
    private static T? GetParameter<T>(
        Dictionary<string, object>? parameters, 
        string camelCase, 
        string snakeCase,
        Func<object, T?> converter) where T : struct
    {
        if (parameters == null) return null;
        
        if (parameters.ContainsKey(camelCase))
        {
            try
            {
                return converter(parameters[camelCase]);
            }
            catch
            {
                return null;
            }
        }
        
        if (parameters.ContainsKey(snakeCase))
        {
            try
            {
                return converter(parameters[snakeCase]);
            }
            catch
            {
                return null;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Gets a string parameter value supporting both camelCase and snake_case naming conventions
    /// </summary>
    private static string? GetStringParameterInternal(
        Dictionary<string, object>? parameters, 
        string camelCase, 
        string snakeCase)
    {
        if (parameters == null) return null;
        
        if (parameters.ContainsKey(camelCase))
        {
            return parameters[camelCase]?.ToString();
        }
        
        if (parameters.ContainsKey(snakeCase))
        {
            return parameters[snakeCase]?.ToString();
        }
        
        return null;
    }

    public static Guid? GetGuidParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter<Guid>(parameters, camelCase, snakeCase, 
            obj => 
            {
                var str = obj?.ToString();
                if (string.IsNullOrWhiteSpace(str)) return null;
                return Guid.TryParse(str, out var guid) ? guid : null;
            });
    }

    public static string? GetStringParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetStringParameterInternal(parameters, camelCase, snakeCase);
    }

    public static decimal? GetDecimalParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter<decimal>(parameters, camelCase, snakeCase, 
            obj => 
            {
                if (obj == null) return null;
                
                // Handle JsonElement
                if (obj is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.Number)
                    {
                        if (jsonElement.TryGetDecimal(out var decimalValue))
                            return decimalValue;
                    }
                }
                
                try
                {
                    return Convert.ToDecimal(obj);
                }
                catch
                {
                    return null;
                }
            });
    }

    public static int? GetIntParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter<int>(parameters, camelCase, snakeCase, 
            obj => 
            {
                if (obj == null) return null;
                
                // Handle JsonElement (common when deserializing JSON)
                if (obj is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.Number)
                    {
                        if (jsonElement.TryGetInt32(out var intValue))
                            return intValue;
                    }
                }
                
                try
                {
                    return Convert.ToInt32(obj);
                }
                catch
                {
                    return null;
                }
            });
    }

    public static DateTime? GetDateTimeParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter<DateTime>(parameters, camelCase, snakeCase, 
            obj => 
            {
                var str = obj?.ToString();
                if (string.IsNullOrWhiteSpace(str)) return null;
                return DateTime.TryParse(str, out var date) ? date : null;
            });
    }

    public static bool? GetBoolParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter<bool>(parameters, camelCase, snakeCase, 
            obj => 
            {
                if (obj == null) return null;
                
                // Handle JsonElement
                if (obj is JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == JsonValueKind.True)
                        return true;
                    if (jsonElement.ValueKind == JsonValueKind.False)
                        return false;
                }
                
                try
                {
                    return Convert.ToBoolean(obj);
                }
                catch
                {
                    return null;
                }
            });
    }

    /// <summary>
    /// Converts camelCase to snake_case
    /// </summary>
    public static string ToSnakeCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        
        return string.Concat(
            camelCase.Select((c, i) => i > 0 && char.IsUpper(c) 
                ? "_" + c.ToString() 
                : c.ToString())
        ).ToLower();
    }

    /// <summary>
    /// Gets the category description with available options from the database
    /// </summary>
    private static string GetCategoryDescription(ApplicationDbContext? dbContext)
    {
        if (dbContext == null)
            return "Filter by category (partial match)";

        try
        {
            var categories = dbContext.ArtworkSales
                .Select(a => a.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            return categories.Any()
                ? $"Filter by category (partial match). Available options: {string.Join(", ", categories)}"
                : "Filter by category (partial match)";
        }
        catch
        {
            return "Filter by category (partial match)";
        }
    }

    /// <summary>
    /// Creates OpenAPI property definitions with snake_case naming
    /// </summary>
    public static Dictionary<string, object> CreateOpenApiProperties(ApplicationDbContext? dbContext = null)
    {
        var categoryDescription = GetCategoryDescription(dbContext);

        return new Dictionary<string, object>
        {
            ["artist_id"] = new { type = "string", description = "Filter by artist ID (exact match)" },
            ["name"] = new { type = "string", description = "Filter by artwork name (partial match)" },
            ["min_height"] = new { type = "number", description = "Minimum height in cm" },
            ["max_height"] = new { type = "number", description = "Maximum height in cm" },
            ["min_width"] = new { type = "number", description = "Minimum width in cm" },
            ["max_width"] = new { type = "number", description = "Maximum width in cm" },
            ["year_created_from"] = new { type = "integer", description = "Start year for creation year filter" },
            ["year_created_to"] = new { type = "integer", description = "End year for creation year filter" },
            ["sale_date_from"] = new { type = "string", format = "date-time", description = "Start date for sale date filter (ISO 8601 format)" },
            ["sale_date_to"] = new { type = "string", format = "date-time", description = "End date for sale date filter (ISO 8601 format)" },
            ["technique"] = new { type = "string", description = "Filter by technique (partial match)" },
            ["category"] = new { type = "string", description = categoryDescription },
            ["currency"] = new { type = "string", description = "Filter by currency (exact match)" },
            ["min_low_estimate"] = new { type = "number", description = "Minimum low estimate" },
            ["max_low_estimate"] = new { type = "number", description = "Maximum low estimate" },
            ["min_high_estimate"] = new { type = "number", description = "Minimum high estimate" },
            ["max_high_estimate"] = new { type = "number", description = "Maximum high estimate" },
            ["min_hammer_price"] = new { type = "number", description = "Minimum hammer price" },
            ["max_hammer_price"] = new { type = "number", description = "Maximum hammer price" },
            ["sold"] = new { type = "boolean", description = "Filter by sold status" },
            ["page"] = new { type = "integer", description = "Page number (default: 1)" }
        };
    }
}