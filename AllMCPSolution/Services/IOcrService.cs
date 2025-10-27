namespace AllMCPSolution.Services;

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(Stream imageStream, CancellationToken ct = default);
}