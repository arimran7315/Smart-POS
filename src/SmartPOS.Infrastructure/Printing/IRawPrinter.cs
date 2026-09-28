namespace SmartPOS.Infrastructure.Printing;

/// <summary>Submits raw bytes to an installed operating-system printer queue.</summary>
public interface IRawPrinter
{
    void Print(string printerName, string documentName, byte[] data);
}
