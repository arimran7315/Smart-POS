namespace SmartPOS.Core.Services;

/// <summary>Formats one CSV cell and neutralizes spreadsheet formulas in untrusted text values.</summary>
public static class CsvCellEncoder
{
    public static string Encode(string value, bool isUntrustedText)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (isUntrustedText)
        {
            var firstContent = value.TrimStart(' ', '\t', '\r', '\n');
            if (firstContent.Length > 0 && firstContent[0] is '=' or '+' or '-' or '@')
                value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
