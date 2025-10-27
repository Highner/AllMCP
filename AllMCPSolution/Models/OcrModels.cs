namespace AllMCPSolution.Models;

public sealed class OcrLine
{
    public string Text { get; set; } = "";
    public List<(int X, int Y)> Polygon { get; set; } = new();
}

public sealed class OcrResult
{
    public string ModelVersion { get; set; } = "";
    public List<OcrLine> Lines { get; set; } = new();
}