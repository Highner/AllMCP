
namespace AllMCPSolution.Tools;

public static class ParameterHelpers
{
    /// <summary>
    /// Gets a parameter value supporting both camelCase and snake_case naming conventions
    /// </summary>
    public static T? GetParameter<T>(
        Dictionary<string, object>? parameters, 
        string camelCase, 
        string snakeCase,
        Func<object, T> converter)
    {
        if (parameters == null) return default;
        
        if (parameters.ContainsKey(camelCase))
            return converter(parameters[camelCase]);
        
        if (parameters.ContainsKey(snakeCase))
            return converter(parameters[snakeCase]);
        
        return default;
    }

    public static Guid? GetGuidParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => Guid.Parse(obj?.ToString() ?? string.Empty));
    }

    public static string? GetStringParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => obj?.ToString());
    }

    public static decimal? GetDecimalParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => Convert.ToDecimal(obj));
    }

    public static int? GetIntParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => Convert.ToInt32(obj));
    }

    public static DateTime? GetDateTimeParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => DateTime.Parse(obj?.ToString() ?? string.Empty));
    }

    public static bool? GetBoolParameter(Dictionary<string, object>? parameters, string camelCase, string snakeCase)
    {
        return GetParameter(parameters, camelCase, snakeCase, 
            obj => Convert.ToBoolean(obj));
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
    /// Creates OpenAPI property definitions with snake_case naming
    /// </summary>
    public static Dictionary<string, object> CreateOpenApiProperties()
    {
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
            ["category"] = new { type = "string", description = "Filter by category (partial match)" },
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
