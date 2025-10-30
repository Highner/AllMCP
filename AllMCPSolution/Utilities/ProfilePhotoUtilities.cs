using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace AllMCPSolution.Utilities;

public static class ProfilePhotoUtilities
{
    private const long MaxPhotoBytes = 4 * 1024 * 1024; // 4 MB
    private const int MaxDimension = 100; // target bounding box 100x100

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public static async Task<ProfilePhotoPayload> ReadAndValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file was empty.");
        }

        if (file.Length > MaxPhotoBytes)
        {
            throw new InvalidOperationException($"Profile photos must be smaller than {MaxPhotoBytes / (1024 * 1024)} MB.");
        }

        var normalizedContentType = NormalizeContentType(file.ContentType);
        if (normalizedContentType is null)
        {
            throw new InvalidOperationException("Unsupported image format. Please upload a JPEG, PNG, or WebP file.");
        }

        await using var originalStream = new MemoryStream();
        await file.CopyToAsync(originalStream, ct);
        if (originalStream.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file was empty.");
        }
        originalStream.Position = 0;

        try
        {
            using var image = await Image.LoadAsync(originalStream, ct);

            var width = image.Width;
            var height = image.Height;

            // Calculate target size preserving aspect ratio, do not upscale
            var scale = Math.Min(1.0,
                Math.Min((double)MaxDimension / Math.Max(1, width), (double)MaxDimension / Math.Max(1, height)));

            if (scale < 1.0)
            {
                var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
                var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

                image.Mutate(op => op.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3,
                    Position = AnchorPositionMode.Center
                }));
            }

            // Re-encode using same format/content type with sane defaults
            var (encoder, outContentType) = GetEncoderForContentType(normalizedContentType);

            await using var outStream = new MemoryStream();
            await image.SaveAsync(outStream, encoder, ct);
            var bytes = outStream.ToArray();
            return new ProfilePhotoPayload(bytes, outContentType);
        }
        catch (UnknownImageFormatException)
        {
            throw new InvalidOperationException("We couldn't read that image. Please upload a valid JPEG, PNG, or WebP file.");
        }
        catch (Exception)
        {
            throw new InvalidOperationException("We couldn't process that image. Please try a different photo.");
        }
    }

    public static string? CreateDataUrl(byte[]? photoBytes, string? contentType)
    {
        if (photoBytes is null || photoBytes.Length == 0)
        {
            return null;
        }

        var normalizedContentType = NormalizeContentType(contentType);
        var effectiveContentType = normalizedContentType ?? "image/jpeg";

        var base64 = Convert.ToBase64String(photoBytes);
        return $"data:{effectiveContentType};base64,{base64}";
    }

    private static (IImageEncoder encoder, string contentType) GetEncoderForContentType(string normalizedContentType)
    {
        switch (normalizedContentType)
        {
            case "image/png":
                return (new PngEncoder
                {
                    ColorType = PngColorType.Rgb
                }, "image/png");
            case "image/webp":
                return (new WebpEncoder
                {
                    Method = WebpEncodingMethod.Default,
                    Quality = 80
                }, "image/webp");
            case "image/jpeg":
            default:
                return (new JpegEncoder
                {
                    Quality = 80,
                    ColorType = JpegEncodingColor.YCbCrRatio444
                }, "image/jpeg");
        }
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var trimmed = contentType.Trim();
        if (AllowedContentTypes.Contains(trimmed))
        {
            return trimmed.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : trimmed.ToLowerInvariant();
        }

        var lowered = trimmed.ToLowerInvariant();
        return lowered switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ => null
        };
    }
}

public sealed record ProfilePhotoPayload(byte[] Bytes, string ContentType);
