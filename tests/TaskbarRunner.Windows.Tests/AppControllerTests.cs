using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskbarRunner;
using TaskbarRunner.Core;
using TaskbarRunner.Platform;
using TaskbarRunner.Rendering;
using Xunit;

// 実際のトレイに渡すコマンド、WPFのキーイベント、タイマー、ファイル保存を一緒に確認する。
// 明示的に有効化したときだけ、デスクトップのフォーカスを動かして検証する。
[Collection("Desktop")]
[Trait("Category", "Desktop")]
public sealed class AppControllerTests(DesktopFixture desktop)
{
    [Fact(Skip = "Set TASKBARRUNNER_DESKTOP_TESTS=1 to run desktop tests",
        SkipUnless = nameof(DesktopFixture.IsEnabled), SkipType = typeof(DesktopFixture))]
    public Task EscapePausesTheRunAndGivingUpRecordsIt() => desktop.RunAsync(() => WithController((controller, folder) =>
    {
        Assert.True(!Overlay.IsVisible, "Startup must leave the overlay hidden");
        Play(controller);
        KeyDown(Key.Escape);
        Assert.True(!File.Exists(Path.Combine(folder, "save.json")), "Leaving Ready must not record a run");
        Play(controller);
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(250));
        KeyDown(Key.Escape);
        Assert.True(!Overlay.IsVisible, "Escape must hide the overlay");
        Assert.True(!File.Exists(Path.Combine(folder, "save.json")),
            "Escape must pause the run instead of recording it");
        Play(controller);
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(250));
        KeyDown(Key.Escape);
        Assert.True(!File.Exists(Path.Combine(folder, "save.json")),
            "Resuming and pausing again must still not record the run");
        Play(controller);
        KeyDown(Key.R);
        var first = LoadSave(folder);
        Assert.True(first.TotalRuns == 1 && first.TotalDistance > 0 && first.BestScore > 0,
            $"Giving up a paused run must record exactly that one run: {first}");
        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.True(LoadSave(folder) == first, "Ready must not continue updating or saving");
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(100));
        KeyDown(Key.Escape);
        controller.Dispose();
        var second = LoadSave(folder);
        Assert.True(second.TotalRuns == 2 && second.BestScore == first.BestScore && second.TotalDistance >= first.TotalDistance,
            $"Disposal must record the paused run and keep the earlier totals: {second}");
        controller.Dispose();
        Assert.True(LoadSave(folder) == second, "Repeated disposal must not count an already finished run again");
    }));

    [Fact(Skip = "Set TASKBARRUNNER_DESKTOP_TESTS=1 to run desktop tests",
        SkipUnless = nameof(DesktopFixture.IsEnabled), SkipType = typeof(DesktopFixture))]
    public Task FocusLossPausesTheRunAndDisposalSavesItOnce() => desktop.RunAsync(() => WithController((controller, folder) =>
    {
        Play(controller);
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(150));
        var other = new Window { Title = "Taskbar Runner controller focus test", Width = 240, Height = 100 };
        try
        {
            other.Show();
            other.Activate();
            Pump();
            Assert.True(other.IsActive, "The other window must actually acquire focus");
            Assert.True(!Overlay.IsVisible, "Focus loss must hide the overlay through AppController");
            Assert.True(!File.Exists(Path.Combine(folder, "save.json")),
                "Focus loss must pause the run instead of recording it");
            controller.Dispose();
            var saved = LoadSave(folder);
            Assert.True(saved.TotalRuns == 1 && saved.TotalDistance > 0,
                $"Disposal must record the paused run exactly once: {saved}");
            controller.Dispose();
            Assert.True(LoadSave(folder) == saved, "Disposal after focus loss must not save a second run");
        }
        finally { other.Close(); }
    }));

    [Fact(Skip = "Set TASKBARRUNNER_DESKTOP_TESTS=1 to run desktop tests",
        SkipUnless = nameof(DesktopFixture.IsEnabled), SkipType = typeof(DesktopFixture))]
    public Task SettingsSaveCancelAndReloadUseTheLocalStores() => desktop.RunAsync(() => WithController((controller, folder) =>
    {
        WithSettings(controller, window =>
        {
            Assert.True(((ComboBox)window.FindName("Fps")).Items.Count == 2, "Only 30 and 60 FPS may be selected");
            ((ComboBox)window.FindName("Fps")).SelectedIndex = 0;
            ((ComboBox)window.FindName("Speed")).SelectedIndex = 2;
            ((ComboBox)window.FindName("CharacterSize")).SelectedIndex = 2;
            ((Slider)window.FindName("Offset")).Value = 17;
            FindButton(window, isDefault: true).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var store = new JsonStore<GameSettings>(Path.Combine(folder, "settings.json"));
        var expected = new GameSettings { FpsLimit = 30, GameSpeed = 1.25, CharacterScale = 1.25, DisplayOffset = 17 };
        Assert.True(store.Load() == expected && store.LastError is null, "Save must persist every control's selected value");
        WithSettings(controller, window =>
        {
            Assert.True(((ComboBox)window.FindName("Fps")).SelectedIndex == 0 &&
                ((ComboBox)window.FindName("Speed")).SelectedIndex == 2 &&
                ((ComboBox)window.FindName("CharacterSize")).SelectedIndex == 2 &&
                ((Slider)window.FindName("Offset")).Value == 17,
                "Reopening settings must show the accepted values");
            ((Slider)window.FindName("Offset")).Value = 50;
            window.Close();
        });
        Assert.True(store.Load() == expected, "Closing without Save must leave the file unchanged");
        Play(controller);
        Assert.True(TaskbarLocator.TryLocate(out var placement, out _), "Taskbar must be available");
        Assert.True(NativeMethods.GetWindowRect(Overlay.Handle, out var rect), "Overlay rectangle must be available");
        Assert.True(rect.Bottom == placement.Top - (int)Math.Round(expected.DisplayOffset * placement.DpiScale),
            "Accepted settings must reposition the live overlay");
        KeyDown(Key.Escape);
        controller.Dispose();
        // 別のControllerを作り、メモリに残った値ではなく保存ファイルから読み戻すことを確認する。
        Assert.True(store.Save(expected with { FpsLimit = 120 }), "Legacy settings fixture must be writable");
        using var reloaded = new AppController(folder, notifyStartup: false);
        WithSettings(reloaded, window =>
        {
            Assert.True(((ComboBox)window.FindName("Fps")).SelectedIndex == 1,
                "A saved legacy 120 FPS value must load as 60 FPS");
            Assert.True(((Slider)window.FindName("Offset")).Value == expected.DisplayOffset,
                "Reload must retain the saved offset");
            FindButton(window, isDefault: true).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(store.Load() == (expected with { FpsLimit = 60 }), "Saving migrated settings must persist 60 FPS");
    }));

    [Fact(Skip = "Set TASKBARRUNNER_DESKTOP_TESTS=1 to run desktop tests",
        SkipUnless = nameof(DesktopFixture.IsEnabled), SkipType = typeof(DesktopFixture))]
    public Task RestartRebindsInputAndDisposalFinishesAnActiveRun() => desktop.RunAsync(() => WithController((controller, folder) =>
    {
        Play(controller);
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(150));
        var original = Overlay;
        controller.Commands.RestartOverlay();
        Pump();
        Assert.True(!ReferenceEquals(original, Overlay) && !Overlay.IsVisible,
            "Restart must replace the window and leave it hidden");
        Assert.True(!File.Exists(Path.Combine(folder, "save.json")),
            "Restart must keep the run paused instead of recording it");
        Play(controller);
        KeyDown(Key.Space);
        PumpFor(TimeSpan.FromMilliseconds(150));
        KeyDown(Key.Escape);
        Play(controller);
        KeyDown(Key.R);
        Assert.True(LoadSave(folder).TotalRuns == 1, "The replacement overlay must still drive the paused run");
        KeyDown(Key.Space);
        controller.Dispose();
        Assert.True(LoadSave(folder).TotalRuns == 2, "Space on the replacement overlay must still start a run");
        controller.Dispose();
        Assert.True(LoadSave(folder).TotalRuns == 2, "Disposal must be idempotent");
        Assert.True(!Application.Current.Windows.OfType<OverlayWindow>().Any(), "Disposal must close the overlay");
    }));

    private static OverlayWindow Overlay => Application.Current.Windows.OfType<OverlayWindow>().Single();

    private static void Play(AppController controller)
    {
        controller.Commands.Play();
        Pump();
        Assert.True(Overlay.IsVisible && Overlay.IsActive, "Play must show and activate the overlay");
    }

    private static void KeyDown(Key key)
    {
        var overlay = Overlay;
        overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(overlay)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Pump();
    }

    private static SaveData LoadSave(string folder)
    {
        var path = Path.Combine(folder, "save.json");
        Assert.True(File.Exists(path), "The controller must create save.json");
        var store = new JsonStore<SaveData>(path);
        var data = store.Load();
        Assert.True(store.LastError is null, $"Saved data must be readable: {store.LastError}");
        return data;
    }

    private static void WithSettings(AppController controller, Action<SettingsWindow> action)
    {
        Exception? failure = null;
        var visited = false;
        controller.Commands.Settings();
        // ShowDialogは入れ子のDispatcherを動かすので、その中で実際のコントロールを操作する。
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            SettingsWindow? window = null;
            try
            {
                window = Application.Current.Windows.OfType<SettingsWindow>().Single();
                visited = true;
                action(window);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        }));
        Pump();
        if (failure is not null) throw new InvalidOperationException("Settings interaction failed", failure);
        Assert.True(visited, "The Settings command must open its dialog");
        Assert.True(!Application.Current.Windows.OfType<SettingsWindow>().Any(), "The settings dialog must close");
    }

    private static Button FindButton(DependencyObject parent, bool isDefault)
        => Descendants(parent).OfType<Button>().Single(button => button.IsDefault == isDefault);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void WithController(Action<AppController, string> test)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TaskbarRunnerControllerTests"));
        var folder = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        try
        {
            using var controller = new AppController(folder, notifyStartup: false);
            test(controller, folder);
        }
        finally
        {
            if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

}
