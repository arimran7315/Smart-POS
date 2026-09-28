using SmartPOS.Core.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class EscPosReceiptEncoderTests
{
    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 42)]
    [InlineData(57, 32)]
    [InlineData(59, 32)]
    [InlineData(60, 42)]
    public void ColumnsForRollWidth_Selects58Or80MillimeterLayout(double paperWidthMm, int expectedColumns)
    {
        Assert.Equal(expectedColumns, EscPosReceiptEncoder.ColumnsForRollWidth(paperWidthMm));
    }

    [Fact]
    public void Encode_EmitsInitializationFormattingAndCutCommands()
    {
        var bytes = EscPosReceiptEncoder.Encode(
        [
            new ReceiptTextLine("SmartPOS", Bold: true, Center: true),
            new ReceiptTextLine("TOTAL: 12.50")
        ], 32);

        Assert.Equal(new byte[] { 0x1B, 0x40 }, bytes[..2]);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x1B, 0x45, 1 }) >= 0);
        Assert.Contains("SmartPOS", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal(new byte[] { 0x1D, 0x56, 0 }, bytes[^3..]);
    }

    [Fact]
    public void Encode_WrapsToPaperWidthAndTransliteratesUnsupportedCharacters()
    {
        var bytes = EscPosReceiptEncoder.Encode([new ReceiptTextLine("Café 商品 123456")], 24);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("Cafe", text);
        Assert.Contains("?", text);
        Assert.Contains("123456", text);
        var receipt = text[(text.IndexOf("Cafe", StringComparison.Ordinal))..text.IndexOf("\u001bd", StringComparison.Ordinal)];
        Assert.All(receipt.Split('\n', StringSplitOptions.RemoveEmptyEntries), line => Assert.InRange(line.Length, 1, 24));
    }

    [Fact]
    public void Encode_RejectsUnsupportedColumnWidths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EscPosReceiptEncoder.Encode([], 10));
    }
}
