using System.IO;

namespace SmartPOS.App.Services;

public static class SmartPosPaths
{
    public const string DataDirectoryEnvironmentVariable = "SMARTPOS_DATA_DIRECTORY";
    public static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPOS")
                : Path.GetFullPath(overridePath);
        }
    }
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public static string ProductImagesDirectory => Path.Combine(DataDirectory, "ProductImages");
}
