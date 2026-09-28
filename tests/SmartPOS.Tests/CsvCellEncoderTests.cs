using SmartPOS.Core.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class CsvCellEncoderTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")", "\"'=HYPERLINK(\"\"https://example.invalid\"\")\"")]
    [InlineData("+cmd|'/C calc'!A0", "'+cmd|'/C calc'!A0")]
    [InlineData("-1 + 2", "'-1 + 2")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData(" \t=1+1", "' \t=1+1")]
    public void Encode_NeutralizesFormulaLikeText(string input, string expected) =>
        Assert.Equal(expected, CsvCellEncoder.Encode(input, isUntrustedText: true));

    [Fact]
    public void Encode_PreservesNegativeNumericValuesAsNumbers() =>
        Assert.Equal("-12.50", CsvCellEncoder.Encode("-12.50", isUntrustedText: false));

    [Fact]
    public void Encode_QuotesCsvSeparatorsAndEscapesQuotes() =>
        Assert.Equal("\"Supplier, \"\"A\"\"\r\nLtd\"", CsvCellEncoder.Encode("Supplier, \"A\"\r\nLtd", isUntrustedText: true));
}
