namespace SmartPOS.Infrastructure.Services;

/// <summary>A validated business rule failure whose message is safe to show to a user.</summary>
public sealed class PosRuleException(string message) : InvalidOperationException(message);

internal static class ServiceInputValidation
{
    public static void RequireDecimalScale(decimal value, string field, int maximumScale = 4)
    {
        if (decimal.Round(value, maximumScale, MidpointRounding.AwayFromZero) != value)
            throw new PosRuleException($"{field} supports up to {maximumScale} decimal places.");
    }

    public static void RequireMaxLength(string? value, string field, int maximum)
    {
        if (value is not null && value.Length > maximum)
            throw new PosRuleException($"{field} must be {maximum} characters or fewer.");
    }

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
