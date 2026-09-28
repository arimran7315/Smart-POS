using SmartPOS.App.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SmartPosPathsTests
{
    [Fact]
    public void DataDirectory_UsesConfiguredOverrideAndKeepsDerivedFilesTogether()
    {
        var original = Environment.GetEnvironmentVariable(SmartPosPaths.DataDirectoryEnvironmentVariable);
        var root = Path.Combine(Path.GetTempPath(), "SmartPOS-isolated-test", "data");
        try
        {
            Environment.SetEnvironmentVariable(SmartPosPaths.DataDirectoryEnvironmentVariable, root);
            Assert.Equal(Path.GetFullPath(root), SmartPosPaths.DataDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "logs"), SmartPosPaths.LogsDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "ProductImages"), SmartPosPaths.ProductImagesDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmartPosPaths.DataDirectoryEnvironmentVariable, original);
        }
    }
}
