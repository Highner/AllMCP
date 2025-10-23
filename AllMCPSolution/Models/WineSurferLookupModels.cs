using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AllMCPSolution.Models;

public sealed record WineSurferLookupResponse(
    [property: JsonPropertyName("wines")] IReadOnlyList<WineSurferLookupResult> Wines);

public sealed record WineSurferLookupResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("appellation")] string? Appellation,
    [property: JsonPropertyName("subAppellation")] string? SubAppellation);

public sealed record WineSurferLookupError(
    [property: JsonPropertyName("message")] string Message);
