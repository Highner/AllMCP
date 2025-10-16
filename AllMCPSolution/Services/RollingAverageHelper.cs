using System;
using System.Collections.Generic;

namespace AllMCPSolution.Services
{
    public record RollingPoint(DateTime Time, decimal? Value, int CountInWindow);

    public static class RollingAverageHelper
    {
        // Computes rolling averages over a monthly series.
        // monthly: list of (Month, Average, Count) for each month in chronological order, including months with Count = 0.
        // windowMonths: size of the rolling window in months (default 12).
        // weightByCount: if true, weight monthly averages by their counts; if false, simple average over contributing months.
        public static List<RollingPoint> RollingAverage(
            IReadOnlyList<(DateTime Month, decimal Average, int Count)> monthly,
            int windowMonths = 12,
            bool weightByCount = true)
        {
            var result = new List<RollingPoint>(monthly.Count);
            if (monthly.Count == 0 || windowMonths <= 0)
            {
                return result;
            }

            for (int i = 0; i < monthly.Count; i++)
            {
                int start = Math.Max(0, i - (windowMonths - 1));
                if (i < windowMonths - 1)
                {
                    result.Add(new RollingPoint(monthly[i].Month, null, 0));
                    continue;
                }

                decimal sum = 0m;
                int countWeight = 0;
                int contributingMonths = 0;

                for (int j = start; j <= i; j++)
                {
                    var m = monthly[j];
                    if (m.Count > 0)
                    {
                        if (weightByCount)
                        {
                            sum += m.Average * m.Count;
                            countWeight += m.Count;
                        }
                        else
                        {
                            sum += m.Average;
                            contributingMonths += 1;
                        }
                    }
                }

                decimal? rolling = null;
                int windowCount = 0;
                if (weightByCount)
                {
                    if (countWeight > 0)
                    {
                        rolling = sum / countWeight;
                        windowCount = countWeight;
                    }
                }
                else
                {
                    if (contributingMonths > 0)
                    {
                        rolling = sum / contributingMonths;
                        windowCount = contributingMonths;
                    }
                }

                result.Add(new RollingPoint(monthly[i].Month, rolling, windowCount));
            }

            return result;
        }
    }
}
