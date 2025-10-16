using System;

namespace AllMCPSolution.Services;

/// <summary>
/// Helper class for calculating artwork sales performance metrics
/// </summary>
public static class PerformanceCalculator
{
    /// <summary>
    /// Calculates the performance factor of a sale relative to its estimate range using the canonical definition:
    /// (Hammer - LowEstimate) / (HighEstimate - LowEstimate) when HighEstimate > LowEstimate; otherwise undefined.
    /// Returns null for invalid/zero-width estimate ranges.
    /// </summary>
    /// <param name="hammerPrice">The actual hammer price achieved</param>
    /// <param name="lowEstimate">The low estimate for the artwork</param>
    /// <param name="highEstimate">The high estimate for the artwork</param>
    /// <returns>
    /// Performance factor (nullable):
    /// - Less than 0: Below low estimate
    /// - 0 to 1: Within estimate range (0 = low estimate, 1 = high estimate)
    /// - Greater than 1: Above high estimate
    /// - null: invalid estimate range (high <= low)
    /// </returns>
    public static double? CalculatePerformanceFactor(decimal hammerPrice, decimal lowEstimate, decimal highEstimate)
    {
        var pos = EstimatePositionHelper.PositionInEstimateRange(hammerPrice, lowEstimate, highEstimate);
        return pos.HasValue ? (double?)pos.Value : null;
    }
}
