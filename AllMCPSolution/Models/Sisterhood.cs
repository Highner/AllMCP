namespace AllMCPSolution.Models;

public class Sisterhood
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<User> Members { get; set; } = [];
}
