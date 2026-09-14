using System;
using System.Runtime.InteropServices;

namespace TaskbarRunner.Platform;

/// <summary>
/// Windows の機能を C# から呼ぶための定義。タスクバーの位置を調べたり、ウィンドウの設定や操作先を変えたりする。
/// </summary>
internal static class NativeMethods
{
    // タスクバーについて調べるための番号。GetState は自動で隠すかなどの設定、GetTaskbarPos は位置を調べる。
    internal const uint AbmGetState = 4, AbmGetTaskbarPos = 5;
    // AbsAutoHide は「自動的に隠す」設定を調べるための値。AbeBottom はタスクバーが画面の下にあることを表す値。
    // MonitorDefaultToPrimary は、対象の画面が分からないときにメイン画面を使う指定。
    internal const uint AbsAutoHide = 1, AbeBottom = 3, MonitorDefaultToPrimary = 1;
    // ウィンドウの追加設定（クリックへの反応など）を読み書きする、と Windows に伝える番号。
    internal const int GwlExStyle = -20;
    // ウィンドウの追加設定。Transparent はクリックを後ろに通す。ToolWindow は Alt+Tab の切り替え候補から外す。
    // AppWindow はタスクバーにボタンを出す。NoActivate はクリックしても操作先をこのウィンドウに切り替えない。
    internal const long WsExTransparent = 0x20, WsExToolWindow = 0x80,
        WsExAppWindow = 0x40000, WsExNoActivate = 0x08000000;
    // 移動などのときの指定。NoActivate は操作先を変えない。FrameChanged は変更したウィンドウの設定を反映させる。
    internal const uint SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    /// <summary>Windows が使う四角い範囲。左・上・右・下の位置で表す。右端と下端のピクセルは範囲に含めない。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>タスクバーについて Windows とやり取りする情報。Size に、この情報全体のバイト数を入れてから渡す。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage, Edge;
        public Rect Rect;
        public nint Param;
    }

    /// <summary>画面の情報。Monitor は画面全体、Work はタスクバーなどを除いて作業に使える範囲。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    // タスクバーの設定や位置を調べる。
    [DllImport("shell32.dll")]
    internal static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
    // ウィンドウの種類を表す名前で探す。タスクバーの名前は "Shell_TrayWnd"。
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string className, string? title);
    // ウィンドウの位置と大きさを、画面の実際のピクセル数で調べる。
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);
    // ウィンドウがどの画面にあるか調べる。window に0を渡すと、flags で指定した画面を使う。
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    // ウィンドウの表示倍率を DPI という値で調べる。100%なら96、150%なら144が返る。
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);
    // ウィンドウの追加設定を読み書きする。64ビット環境でも値が欠けないよう、LongPtr という名前の機能を使う。
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    // ウィンドウの位置、大きさ、手前に表示する順番を変える。after が-1なら、他の通常のウィンドウより手前に置く。
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);
    // 名前に対応する通知番号を Windows に登録する。"TaskbarCreated" を使うと、タスクバーの作り直しを知らせる番号が分かる。
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);
}
