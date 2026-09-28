using System.Text.RegularExpressions;

namespace SmartPOS.Core.Services;

public static partial class InvoiceNumberFormatter
{
    public const string DefaultFormat = "{PREFIX}-{YEAR}-{SEQUENCE:D6}";

    public static string Format(string? template, string prefix, int year, int sequence)
    {
        template = string.IsNullOrWhiteSpace(template) ? DefaultFormat : template.Trim();
        if (template.Length > 100) throw new FormatException("Invoice number format must be 100 characters or fewer.");
        if (string.IsNullOrWhiteSpace(prefix) || sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!SequencePlaceholder().IsMatch(template))
            throw new FormatException("Invoice number format must contain a {SEQUENCE} placeholder.");

        var result = SequencePlaceholder().Replace(template,
            match => sequence.ToString($"D{(match.Groups["width"].Success ? int.Parse(match.Groups["width"].Value) : 1)}"));
        result = result.Replace("{PREFIX}", prefix.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{YEAR}", year.ToString("D4"), StringComparison.OrdinalIgnoreCase);
        if (result.Contains('{') || result.Contains('}')) throw new FormatException("Invoice number format contains an unsupported placeholder.");
        if (string.IsNullOrWhiteSpace(result) || result.Length > 128) throw new FormatException("Generated invoice number is empty or too long.");
        return result;
    }

    [GeneratedRegex(@"\{SEQUENCE(?::D(?<width>\d{1,2}))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SequencePlaceholder();
}
