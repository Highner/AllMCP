using System.Collections.Generic;

namespace AllMCPSolution.Tools;

public interface IProcessingResult
{
    bool Success { get; }

    string Message { get; }

    IReadOnlyList<string>? Errors { get; }

    object? Suggestions { get; }
}
