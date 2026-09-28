using SmartPOS.Core.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class InvoiceNumberFormatterTests
{
    [Fact]
    public void DefaultFormatProducesMonotonicLookingInvoiceIds()
    {
        Assert.Equal("INV-2026-000001", InvoiceNumberFormatter.Format(null, "INV", 2026, 1));
        Assert.Equal("PUR-2026-000042", InvoiceNumberFormatter.Format("", "PUR", 2026, 42));
    }

    [Fact]
    public void CustomFormatSupportsPrefixYearAndSequencePadding()
    {
        Assert.Equal("SALE/2026/0042", InvoiceNumberFormatter.Format("{PREFIX}/{YEAR}/{SEQUENCE:D4}", "SALE", 2026, 42));
        Assert.Equal("42-INV", InvoiceNumberFormatter.Format("{SEQUENCE}-{PREFIX}", "INV", 2026, 42));
    }

    [Theory]
    [InlineData("INVOICE-{YEAR}")]
    [InlineData("{PREFIX}-{SEQUENCE:D5}-{UNKNOWN}")]
    [InlineData("{PREFIX}-{SEQUENCE:D123}")]
    public void InvalidFormatIsRejected(string format) =>
        Assert.Throws<FormatException>(() => InvoiceNumberFormatter.Format(format, "INV", 2026, 1));
}
