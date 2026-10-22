using SmartPOS.App.ViewModels;
using Xunit;

namespace SmartPOS.Tests;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task IsRunningSignalsStartAndFinishAndBlocksConcurrentExecution()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var command = new AsyncCommand(async () =>
        {
            executions++;
            await completion.Task;
        });
        var runningChanged = new List<bool>();
        command.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(command.IsRunning)) runningChanged.Add(command.IsRunning);
        };

        var execution = command.ExecuteAsync();

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, executions);

        completion.SetResult();
        await execution;

        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        Assert.Equal(new[] { true, false }, runningChanged);
    }
}
