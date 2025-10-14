
namespace AllMCPSolution.Services;

/// <summary>
/// Helper class for calculating artwork sales performance metrics
/// </summary>
public static class PerformanceCalculator
{
    /// <summary>
    /// Calculates the performance factor of a sale relative to its estimate range.
    /// </summary>
    /// <param name="hammerPrice">The actual hammer price achieved</param>
    /// <param name="lowEstimate">The low estimate for the artwork</param>
    /// <param name="highEstimate">The high estimate for the artwork</param>
    /// <returns>
    /// Performance factor:
    /// - Less than 0: Below low estimate (negative factor shows how far below)
    /// - 0 to 1: Within estimate range (0 = low estimate, 1 = high estimate)
    /// - Greater than 1: Above high estimate (factor shows ratio to high estimate)
    /// </returns>
    public static double CalculatePerformanceFactor(decimal hammerPrice, decimal lowEstimate, decimal highEstimate)
    {
        // Handle edge cases where estimates are zero or invalid
        if (lowEstimate == 0 && highEstimate == 0)
            return -1; // No estimate data available
        
        // If hammer price is below low estimate
        if (hammerPrice < lowEstimate)
        {
            if (lowEstimate == 0)
                return -1; // Can't calculate performance if low estimate is zero
            
            // Return negative factor: how far below as a fraction of low estimate
            // E.g., if hammer is 80 and low is 100, factor = -0.2
            return (double)((hammerPrice - lowEstimate) / lowEstimate);
        }

        // If hammer price is above high estimate
        if (hammerPrice > highEstimate)
        {
            if (highEstimate == 0)
                return 1; // Can't calculate performance if high estimate is zero
            
            // Return factor > 1: ratio of hammer price to high estimate
            // E.g., if hammer is 150 and high is 100, factor = 1.5
            return (double)(hammerPrice / highEstimate);
        }

        // If hammer price is within range
        // Map linearly from low (0) to high (1)
        var range = highEstimate - lowEstimate;
        if (range == 0)
            return 0.5; // Edge case: if low == high, return midpoint

        return (double)((hammerPrice - lowEstimate) / range);
    }
}
