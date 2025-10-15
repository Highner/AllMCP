using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AllMCPSolution.Models;

public class InflationIndex
{
    [Key]
    public int Id { get; set; }

    // Year and Month represent the period (UTC, first day of month)
    [Range(1900, 3000)]
    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal IndexValue { get; set; }

    // Convenience computed property (not mapped)
    [NotMapped]
    public DateTime Period => new DateTime(Year, Month, 1);
}