using System.Threading;
using System.Windows;

namespace TaskbarRunner;

/// <summary>
/// アプリの起動と終了を処理する。すでに起動していないか確認してから <see cref="AppController"/> を作る。
/// 起動直後はゲーム画面を出さず、タスクバーの時計の近くにあるアイコンから操作できるようにする。
/// </summary>
public partial class App : Application
{
    // 二重に起動しないよう、Windows に共通の名前の目印（Mutex）を作り、終了するまで残しておく。
    private Mutex? singleInstance;
    private AppController? controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 名前を Local\ で始めると、同じ Windows ログイン内だけで目印を共有する。
        // created が false なら目印がすでにあるので、起動済みと判断する。
        singleInstance = new Mutex(true, @"Local\TaskbarRunner", out var created);
        if (!created)
        {
            MessageBox.Show("Taskbar Runner は起動済みです。通知領域のアイコンから Play を選んでください。",
                "Taskbar Runner", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        controller = new AppController();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
