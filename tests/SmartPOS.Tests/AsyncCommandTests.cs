using SmartPOS.App.ViewModels;
using Xunit;

namespace SmartPOS.Tests;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsFailureAndReenablesCommand()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("The operation failed.");
        Exception? reported = null;
        var command = new AsyncCommand(async () =>
        {
            started.SetResult(true);
            await release.Task;
            throw failure;
        }, onError: exception => reported = exception);

        var execution = command.ExecuteAsync();
        await started.Task;

        Assert.False(command.CanExecute(null));
        release.SetResult(true);
        await execution;

        Assert.Same(failure, reported);
        Assert.True(command.CanExecute(null));
    }
}
