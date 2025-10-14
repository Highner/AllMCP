using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using AllMCPSolution.Models;

namespace AllMCPSolution.Repositories;

public static class ArtworkSaleParser
{
    public static List<ArtworkSale> ParseFromStream(Stream htmlStream)
    {
        var doc = new HtmlDocument();
        doc.Load(htmlStream);

        var results = new List<ArtworkSale>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lots = doc.DocumentNode
                       .SelectNodes("//div[contains(@class,'lots-list') and contains(@class,'lots-tile')]//div[contains(@class,'lots-tile-access-wide')]")
                   ?? new HtmlNodeCollection(null);var category = ExtractCategory(doc);

        foreach (var lot in lots)
{
    try
    {
        // ---- Gather identifying info early (for deduping) ----
        var titleText = lot.SelectSingleNode(".//div[contains(@class,'lot-title')]")?.InnerText?.Trim() ?? "";
        var dateText  = lot.SelectSingleNode(".//div[contains(@class,'lot-date')]")?.InnerText?.Trim() ?? "";
        var sizeText  = lot.SelectSingleNode(".//div[contains(@class,'size')]/span")?.InnerText?.Trim() ?? "";

        // Unique key: title + date + size
        var key = $"{titleText}|{dateText}|{sizeText}";
        if (!seen.Add(key))
            continue; // skip duplicates

        // ---- Parse main fields ----
        var (name, year) = ParseTitleAndYear(titleText);

        var sale = new ArtworkSale
        {
            Name = HtmlEntity.DeEntitize(name),
            YearCreated = year ?? 0,
            Technique = lot.SelectSingleNode(".//div[contains(@class,'technique')]")?.InnerText?.Trim(),
            Category = category
        };

        // Size: "43.2 x 34.9 cm"
        (sale.Height, sale.Width) = ParseSize(sizeText);

        // Sale date (last date in a range)
        sale.SaleDate = ParseSaleDate(dateText);

        // Estimates
        var estText = lot.SelectSingleNode(".//div[contains(@class,'estimation')]")?.InnerText ?? "";
        var (cur, low, high) = ParseEstimate(estText);
        sale.LowEstimate = low;
        sale.HighEstimate = high;

        // Hammer price / Sold flag
        var priceText = lot.SelectSingleNode(".//div[contains(@class,'prices')]")?.InnerText ?? "";
        if (priceText.Contains("Not sold", StringComparison.OrdinalIgnoreCase))
        {
            sale.Sold = false;
        }
        else
        {
            sale.Sold = true;
            sale.HammerPrice = ParseMoneyAfterLabel(priceText, "Hammer price:");
        }

        // Currency (prefer price currency if present)
        sale.Currency = MapCurrencySymbolToCode(DetectCurrencySymbol(priceText) ?? cur ?? "");
        
        sale.Id = Guid.NewGuid();

        // Add to results
        results.Add(sale);
    }
    catch
    {
        // Skip malformed lots, but continue processing others
    }
}

        return results;
    }

    // ===== helper methods (same as before) =====

    private static (string name, int? year) ParseTitleAndYear(string title)
    {
        var m = Regex.Match(title, @"^(.*?)(?:\s*\((\d{4})\))?$");
        return m.Success
            ? (m.Groups[1].Value.Trim(), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : (int?)null)
            : (title, null);
    }

    private static (decimal, decimal) ParseSize(string size)
    {
        var m = Regex.Match(size, @"([\d\.,]+)\s*x\s*([\d\.,]+)");
        if (!m.Success) return (0, 0);
        decimal toDec(string s) => decimal.TryParse(s.Replace(",", "").Trim(),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return (toDec(m.Groups[1].Value), toDec(m.Groups[2].Value));
    }

    private static DateTime ParseSaleDate(string text)
    {
        var last = text.Split('-').Last().Trim();
        return DateTime.TryParse(last, new CultureInfo("en-GB"), DateTimeStyles.None, out var d)
            ? d
            : DateTime.MinValue;
    }

    private static (string cur, decimal low, decimal high) ParseEstimate(string html)
    {
        var clean = HtmlEntity.DeEntitize(html);
        var m = Regex.Match(clean, @"Estimate:\s*([\p{Sc}A-Z]{1,3})?\s*([\d\.,]+)\s*-\s*([\p{Sc}A-Z]{1,3})?\s*([\d\.,]+)");
        if (!m.Success) return (null, 0, 0);
        string cur = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[1].Value;
        decimal toMoney(string s) => decimal.TryParse(s.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return (cur, toMoney(m.Groups[2].Value), toMoney(m.Groups[4].Value));
    }

    private static decimal ParseMoneyAfterLabel(string text, string label)
    {
        var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        var m = Regex.Match(text.Substring(idx + label.Length), @"([\d\.,]+)");
        return m.Success ? decimal.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : 0;
    }

    private static string DetectCurrencySymbol(string text)
    {
        var m = Regex.Match(HtmlEntity.DeEntitize(text), @"([\p{Sc}]|HK\$|US\$|CNY|JPY|CHF|EUR|GBP|\$|£|€)");
        return m.Success ? m.Value : null;
    }

    private static string MapCurrencySymbolToCode(string symbol) =>
        symbol switch
        {
            null or "" => "",
            "€" or "EUR" => "EUR",
            "£" or "GBP" => "GBP",
            "$" or "US$" => "USD",
            "HK$" => "HKD",
            "¥" or "JPY" => "JPY",
            _ => symbol
        };

    private static string ExtractCategory(HtmlDocument doc)
    {
        var h1 = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'lots-list-header')]//h1")?.InnerText ?? "";
        var m = Regex.Match(h1, @"in\s+(?<cat>[A-Za-z\-\s]+)$");
        return m.Success ? m.Groups["cat"].Value.Trim() : "";
    }
}