using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AllMCPSolution.Utilities;

public static class ProfilePhotoUtilities
{
    private const long MaxPhotoBytes = 4 * 1024 * 1024; // 4 MB

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

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);

        if (memory.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file was empty.");
        }

        return new ProfilePhotoPayload(memory.ToArray(), normalizedContentType);
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
