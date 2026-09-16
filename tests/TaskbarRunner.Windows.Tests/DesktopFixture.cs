using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Xunit;

[CollectionDefinition("Desktop", DisableParallelization = true)]
public sealed class DesktopCollection : ICollectionFixture<DesktopFixture>;

public sealed class DesktopFixture : IAsyncDisposable
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("TASKBARRUNNER_DESKTOP_TESTS") == "1";
    private readonly Lazy<Task<Dispatcher>> dispatcher = new(StartDispatcher);

    public async Task RunAsync(Action action)
    {
        var ui = await dispatcher.Value;
        await ui.InvokeAsync(action);
    }

    private static Task<Dispatcher> StartDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.SetResult(app.Dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "TaskbarRunner desktop tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (!dispatcher.IsValueCreated) return;
        var ui = await dispatcher.Value;
        await ui.InvokeAsync(() => Application.Current.Shutdown());
    }
}
