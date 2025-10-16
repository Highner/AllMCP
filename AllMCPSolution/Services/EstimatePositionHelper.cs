namespace AllMCPSolution.Services
{
    /// <summary>
    /// Shared helper for computing the normalized position of a hammer price within the estimate range.
    /// </summary>
    public static class EstimatePositionHelper
    {
        /// <summary>
        /// Computes (Hammer - LowEstimate) / (HighEstimate - LowEstimate) when HighEstimate > LowEstimate.
        /// Returns null if the estimate range is invalid or zero-width.
        /// The returned value can be less than 0 (below low) or greater than 1 (above high).
        /// </summary>
        public static decimal? PositionInEstimateRange(decimal hammerPrice, decimal lowEstimate, decimal highEstimate)
        {
            var range = highEstimate - lowEstimate;
            if (range > 0)
            {
                return (hammerPrice - lowEstimate) / range;
            }
            return null;
        }
    }
}