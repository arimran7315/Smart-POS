using System.Text;
using System.Globalization;

namespace SmartPOS.Core.Services;

public sealed record ReceiptTextLine(string Text, bool Bold = false, bool Center = false);

/// <summary>Encodes plain-text thermal receipts using the widely supported ESC/POS command subset.</summary>
public static class EscPosReceiptEncoder
{
    public static int ColumnsForRollWidth(double paperWidthMm) => paperWidthMm is >= 57 and <= 59 ? 32 : 42;

    public static byte[] Encode(IEnumerable<ReceiptTextLine> lines, int columns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (columns is < 24 or > 64) throw new ArgumentOutOfRangeException(nameof(columns));

        using var output = new MemoryStream();
        output.Write(new byte[] { 0x1B, 0x40 }); // Initialize
        var bold = false;
        var centered = false;
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            if (bold != line.Bold)
            {
                output.Write(new byte[] { 0x1B, 0x45, line.Bold ? (byte)1 : (byte)0 });
                bold = line.Bold;
            }
            if (centered != line.Center)
            {
                output.Write(new byte[] { 0x1B, 0x61, line.Center ? (byte)1 : (byte)0 });
                centered = line.Center;
            }

            foreach (var wrappedLine in Wrap(ToPrinterText(line.Text), columns))
            {
                output.Write(Encoding.ASCII.GetBytes(wrappedLine));
                output.WriteByte(0x0A);
            }
        }
        if (bold) output.Write(new byte[] { 0x1B, 0x45, 0 });
        if (centered) output.Write(new byte[] { 0x1B, 0x61, 0 });
        output.Write(new byte[] { 0x1B, 0x64, 4 }); // Feed before cut
        output.Write(new byte[] { 0x1D, 0x56, 0 }); // Full cut; printers without a cutter ignore this
        return output.ToArray();
    }

    private static string ToPrinterText(string text)
    {
        // ESC/POS code pages differ by printer. Emit portable ASCII and transliterate accents.
        var normalized = text.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(character is >= ' ' and <= '~' ? character : '?');
        }
        return result.ToString();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length == 0) { yield return string.Empty; yield break; }
        for (var offset = 0; offset < text.Length; offset += width)
            yield return text.Substring(offset, Math.Min(width, text.Length - offset));
    }
}
