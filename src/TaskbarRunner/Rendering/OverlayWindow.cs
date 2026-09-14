using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarRunner.Core;
using TaskbarRunner.Platform;

namespace TaskbarRunner.Rendering;

/// <summary>
/// タスクバーのすぐ上にゲームを表示する透明ウィンドウ。遊んでいる間だけキーやクリックで操作できるようにする。
/// ウィンドウの位置や、操作を受け付けるかどうかを決める。
/// </summary>
internal sealed class OverlayWindow : Window
{
    // 上は得点や操作説明を出す場所、下はキャラクターと障害物が動く場所。
    private const double HudHeight = 100;
    private const double PlayfieldHeight = 180;
    private readonly GameSurface surface;
    private HwndSource? source;
    // タスクバーを表示する Explorer が再起動したときの通知番号。通知が来たらゲームを置く位置を調べ直す。
    private readonly uint taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    // ゲーム画面を操作できるかどうか。true の間だけキーやクリックを受け付ける。
    private bool interactive;
    private static readonly Brush InputBackground = CreateInputBackground();
    internal nint Handle { get; private set; }
    /// <summary>画面の大きさ、表示倍率、タスクバーに変更があったら、登録された処理を呼んでゲームの位置を調べ直す。</summary>
    internal event Action? EnvironmentChanged;
    /// <summary>操作先が他のウィンドウに切り替わったら、登録された処理を呼んでプレイを中断する。</summary>
    internal event Action? FocusLost;
    internal event Action<Key>? KeyPressed;
    internal event Action<Key>? KeyReleased;

    internal OverlayWindow(GameSession game)
    {
        Title = "Taskbar Runner";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 800;                      // 仮の幅。後で Position が画面に合う幅に変える。
        Height = HudHeight + PlayfieldHeight;
        Content = surface = new GameSurface(game);
        // WPF からも、他のウィンドウへの切り替えを受け取る。ゲームの操作中なら中断を知らせる。
        Deactivated += (_, _) => { if (interactive) FocusLost?.Invoke(); };
        PreviewKeyDown += (_, e) =>
        {
            if (!interactive || !IsActive) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Windows キーを押したら、スタートメニューを操作できるようゲームを中断する。
            if (key is Key.LWin or Key.RWin) { FocusLost?.Invoke(); return; }
            // ゲームで使わないキーは、Windows などがそのまま処理できるようにする。
            if (key is not (Key.Space or Key.Up or Key.Down or Key.Left or Key.Right or Key.Escape)) return;
            e.Handled = true;
            // 押しっぱなしで何度もジャンプしないよう、最初に押したときだけ処理する。
            if (!e.IsRepeat) KeyPressed?.Invoke(key);
        };
        PreviewKeyUp += (_, e) =>
        {
            if (!interactive || !IsActive) return;
            if (e.Key is Key.Space or Key.Up or Key.Down or Key.Left or Key.Right or Key.Escape)
            {
                e.Handled = true;
                KeyReleased?.Invoke(e.Key);
            }
        };
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(Handle);
            source?.AddHook(WindowProcedure);
            SetInteractive(false);
        };
        Closed += (_, _) => source?.RemoveHook(WindowProcedure);
        // 表示する前にも Windows の機能で位置などを変えるので、ウィンドウを指定する番号を作っておく。
        new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>ゲーム画面をタスクバーのすぐ上に置き、設定に合わせてキャラクターの大きさを変える。</summary>
    internal void Position(TaskbarPlacement placement, GameSettings settings)
    {
        // 得点を表示する場所と、キャラクターが動く場所を足して高さを決める。キャラクターを大きくしても、得点の場所は広げない。
        var height = (int)Math.Ceiling((HudHeight + PlayfieldHeight * settings.CharacterScale) * placement.DpiScale);
        var offset = (int)Math.Round(settings.DisplayOffset * placement.DpiScale);
        // タスクバーの位置は画面の実際のピクセル数で得られる。表示倍率で位置がずれないよう、その数値のまま Windows に移動を頼む。
        // 2番目に渡す -1 は、他の通常のウィンドウより手前に表示する指定（HWND_TOPMOST）。
        NativeMethods.SetWindowPos(Handle, new nint(-1), placement.Left,
            placement.Top - height - offset, placement.Width, height, NativeMethods.SwpNoActivate);
        surface.CharacterScale = settings.CharacterScale;
        surface.InvalidateVisual();
    }

    /// <summary>ゲーム画面を出して、キーやクリックで操作できるようにする。操作先をゲームに切り替えられたら true を返す。</summary>
    internal bool BeginInteraction()
    {
        SetInteractive(true);
        Show();
        NativeMethods.SetForegroundWindow(Handle);
        Activate();
        Focus();
        // 他のアプリを操作中だと切り替えに失敗することがあるので、本当にゲームが操作先になったか確かめる。
        return NativeMethods.GetForegroundWindow() == Handle;
    }

    /// <summary>ゲーム画面を隠す。その前に、クリックが後ろのウィンドウに届く設定に戻す。</summary>
    internal void EndInteraction()
    {
        SetInteractive(false);
        Hide();
    }

    internal void Redraw() => surface.InvalidateVisual();

    private static Brush CreateInputBackground()
    {
        // 完全に透明な場所をクリックすると、このウィンドウではなく後ろのウィンドウに届いてしまう。
        // 背景をごく薄く塗り、ほぼ透明に見せながら、ゲーム画面のどこでもクリックを受け取れるようにする。
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private void SetInteractive(bool enabled)
    {
        interactive = enabled;
        Background = enabled ? InputBackground : Brushes.Transparent;
        var style = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GwlExStyle).ToInt64();
        // 遊んでいるかどうかに関係なく、Alt+Tab の切り替え候補やタスクバーには並べない。
        style = (style | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        // 遊んでいる間はゲームを操作できるようにし、それ以外はクリックが後ろのウィンドウに届くようにする。
        if (enabled) style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        else style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GwlExStyle, new nint(style));
        // 変更したウィンドウの設定を Windows に反映させる。0x1（SWP_NOSIZE）は大きさを、0x2（SWP_NOMOVE）は位置を変えない指定。
        NativeMethods.SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0,
            0x1 | 0x2 | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
    }

    // Windows から届く通知を調べ、画面設定の変更や、ゲームの操作を受け付けていない間のクリックに対応する。
    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // 画面サイズ変更は0x007E（WM_DISPLAYCHANGE）、設定変更は0x001A（WM_SETTINGCHANGE）、表示倍率の変更は0x02E0（WM_DPICHANGED）。
        // 通知を処理している途中で位置を変えないよう、位置を調べ直す処理は後で実行する。
        if (message == taskbarCreated || message is 0x007E or 0x001A or 0x02E0)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => EnvironmentChanged?.Invoke()));
        if (!interactive && message == 0x0021) // クリックで操作先にするかの問い合わせ（WM_MOUSEACTIVATE）。
        {
            handled = true;
            return new nint(3); // 操作先を切り替えない、と Windows に返す（MA_NOACTIVATE）。
        }
        return 0;
    }
}
