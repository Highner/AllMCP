// Services/AzureVisionOcrService.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AllMCPSolution.Models;

namespace AllMCPSolution.Services;

public sealed class AzureVisionOcrService : IOcrService
{
    private readonly ImageAnalysisClient _client;
    private readonly ILogger<AzureVisionOcrService> _logger;

    public AzureVisionOcrService(IConfiguration config, ILogger<AzureVisionOcrService> logger)
    {
        _logger = logger;
        var endpoint = config["Vision:Endpoint"] ?? throw new InvalidOperationException("Missing Vision:Endpoint");
        var key = config["Vision:Key"]; // only set locally

        ImageAnalysisClient client;

        if (!string.IsNullOrWhiteSpace(key))
        {
            // Local dev: use API key
            client = new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
        }
        else
        {
            // Azure: use Managed Identity (App Service)
            client = new ImageAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
        }

        _client = client;
    }

    public async Task<OcrResult> ExtractTextAsync(Stream imageStream, CancellationToken ct = default)
    {
        if (imageStream is null || imageStream.Length == 0)
            throw new ArgumentException("Image stream is empty.", nameof(imageStream));
        if (imageStream.CanSeek) imageStream.Position = 0;

        try
        {
            // 1.0.0 style: pass VisualFeatures.Read as a parameter
            ImageAnalysisResult value = await _client.AnalyzeAsync(
                BinaryData.FromStream(imageStream),
                VisualFeatures.Read,
                new ImageAnalysisOptions()  // optional; keep for future toggles
                {
                    // e.g., GenderNeutralCaption = true (for caption feature)
                },
                ct);

            var result = new OcrResult { ModelVersion = value.ModelVersion ?? "" };

            if (value.Read?.Blocks != null)
            {
                foreach (var block in value.Read.Blocks)
                foreach (var line in block.Lines)
                {
                var polygon = line.BoundingPolygon?.Select(p => (p.X, p.Y)).ToList() ?? [];
                    result.Lines.Add(new OcrLine
                    {
                        Text = line.Text ?? "",
                        Polygon = polygon
                    });
                }
            }
            return result;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Vision OCR request failed: {Message}", ex.Message);
            throw;
        }
    }

}