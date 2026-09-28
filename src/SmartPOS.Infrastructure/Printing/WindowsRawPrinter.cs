using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartPOS.Infrastructure.Printing;

/// <summary>Windows spooler transport for printer languages such as ESC/POS.</summary>
public sealed class WindowsRawPrinter : IRawPrinter
{
    public void Print(string printerName, string documentName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Raw print data cannot be empty.", nameof(data));

        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open printer '{printerName}'.");
        var document = new DocInfo { DocumentName = documentName, DataType = "RAW" };
        var documentStarted = false;
        var pageStarted = false;
        try
        {
            if (StartDocPrinter(printer, 1, ref document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the raw printer job.");
            documentStarted = true;
            if (!StartPagePrinter(printer)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the printer page.");
            pageStarted = true;
            if (!WritePrinter(printer, data, data.Length, out var written) || written != data.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The printer did not accept the complete receipt.");
        }
        finally
        {
            if (pageStarted) EndPagePrinter(printer);
            if (documentStarted) EndDocPrinter(printer);
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo documentInfo);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, byte[] data, int count, out int written);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);
}
