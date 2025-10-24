using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using OpenAI.Chat;

namespace AllMCPSolution.Utilities;

public static class StringUtilities
{
    public static string? ResolveDisplayName(string? domainName, string? identityName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(domainName))
        {
            return domainName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(identityName))
        {
            return identityName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim();
        }

        return null;
    }
    
    public static string? NormalizeEmailCandidate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim().ToLowerInvariant();
    }

    public static string EnsureEmailQueryParameter(string link, string email)
    {
        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(email))
        {
            return link;
        }

        var queryIndex = link.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            var query = link.Substring(queryIndex);
            var parsed = QueryHelpers.ParseQuery(query);
            if (parsed.Any(pair => string.Equals(pair.Key, "email", StringComparison.OrdinalIgnoreCase)))
            {
                return link;
            }
        }

        return QueryHelpers.AddQueryString(link, "email", email);
    }

    public static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var trimmed = value.Trim();
            var address = new MailAddress(trimmed);
            return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
    
    public static string? ExtractCompletionText(ChatCompletion completion)
    {
        if (completion?.Content is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in completion.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && !string.IsNullOrWhiteSpace(part.Text))
            {
                builder.Append(part.Text);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }
    
    public static string ExtractJsonSegment(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && closingFence > firstNewLine)
            {
                trimmed = trimmed.Substring(firstNewLine + 1, closingFence - firstNewLine - 1);
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed.Trim();
    }
}