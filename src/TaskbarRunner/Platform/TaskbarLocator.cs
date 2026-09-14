using System.Runtime.InteropServices;

namespace TaskbarRunner.Platform;

/// <summary>
/// ゲーム画面を置く位置。Left・Top・Width は画面の実際のピクセル数。DpiScale は Windows の表示倍率で、100%なら1、150%なら1.5。
/// Top はゲーム画面の下端を合わせる高さ。通常はタスクバーの上端、自動で隠す設定なら画面の下端に合わせる。
/// </summary>
internal readonly record struct TaskbarPlacement(int Left, int Top, int Width, double DpiScale);

/// <summary>メイン画面の下にあるタスクバーを探して、そのすぐ上にゲームを表示する位置を調べる。</summary>
internal static class TaskbarLocator
{
    /// <summary>ゲームを置く位置が分かれば true を返す。分からなければ false を返し、error に画面に表示する理由を入れる。</summary>
    internal static bool TryLocate(out TaskbarPlacement placement, out string error)
    {
        placement = default;
        error = "タスクバーが見つかりません。Explorer の起動後に通知領域のロボットをもう一度左クリックしてください。";
        var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (tray == 0) return false; // タスクバーが見つからなければ、ここでやめる。
        var data = new NativeMethods.AppBarData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(), Window = tray
        };
        // タスクバーを「自動的に隠す」設定か調べる。この設定なら、タスクバーが出たり隠れたりしてもゲームは画面の下端に置く。
        var autoHide = (NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref data) & NativeMethods.AbsAutoHide) != 0;
        // タスクバーの位置を調べる
        var found = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) != 0;
        var monitor = NativeMethods.MonitorFromWindow(0, NativeMethods.MonitorDefaultToPrimary);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return false;
        var rect = data.Rect;
        if (!found && !autoHide && !NativeMethods.GetWindowRect(tray, out rect)) return false;
        var dpi = NativeMethods.GetDpiForWindow(tray);
        // 位置の問い合わせに失敗したら、上下左右の情報は使わない。代わりに、別の方法で得た位置と大きさから判断する。
        if (!TryCalculatePlacement(info.Monitor, rect, found ? data.Edge : null, autoHide, dpi, out placement))
        {
            error = "メインモニター下部のタスクバーに対応しています。タスクバーの配置を確認してください。";
            return false;
        }
        return true;
    }

    // 受け取った画面とタスクバーの情報から、ゲームを置く位置を計算する。テストでは Windows の設定を変えずに確認できる。
    internal static bool TryCalculatePlacement(NativeMethods.Rect monitor, NativeMethods.Rect taskbar,
        uint? edge, bool autoHide, uint dpi, out TaskbarPlacement placement)
    {
        placement = default;
        // タスクバーが画面の上下左右のどこにあるか分かっているなら、下にある場合だけ進める。
        if (edge is not null && edge != NativeMethods.AbeBottom) return false;

        var width = monitor.Right - monitor.Left; // ゲーム画面をメイン画面の横幅いっぱいにする。
        var scale = (dpi == 0 ? 96 : dpi) / 96.0; // 表示倍率が分からなければ100%として計算する。
        if (autoHide)
        {
            placement = new TaskbarPlacement(monitor.Left, monitor.Bottom, width, scale);
            return true;
        }
        // 位置と大きさも確認する。画面の上端に触れているものや、下端まで届いていないものは使わない。
        // 幅が画面の半分より狭いものも、縦置きのタスクバーとみなして使わない。
        // 下端が画面から2ピクセル以内でずれているだけなら、そのまま使う。
        if (taskbar.Top <= monitor.Top || taskbar.Bottom < monitor.Bottom - 2 || taskbar.Right - taskbar.Left < width / 2)
            return false;

        placement = new TaskbarPlacement(monitor.Left, taskbar.Top, width, scale);
        return true;
    }
}
