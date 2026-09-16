using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TaskbarRunner.Platform;

namespace TaskbarRunner.Tray;

/// <summary>
/// タスクバーの時計の近くにロボットのアイコンを出す。左クリックでゲームを開き、右クリックでメニューを出す。
/// 遊ぶ、設定を開く、終了するなどの処理は、アイコンを作るときに受け取った関数を呼ぶ。
/// </summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu;
    private readonly Icon artwork;

    internal TrayController(AppCommands commands)
    {
        artwork = CreateIcon();
        menu = new ContextMenuStrip();
        var version = typeof(TrayController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "unknown";
        menu.Items.Add(new ToolStripMenuItem($"Taskbar Runner · v{version}{(Build.IsDebug ? " · DEBUG" : "")}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("▶  Play", null, (_, _) => commands.Play());
        menu.Items.Add("Settings…", null, (_, _) => commands.Settings());
        menu.Items.Add("Restart Overlay", null, (_, _) => commands.RestartOverlay());
        // 間違えて終了を押しにくいよう、他の項目との間に線を入れる。
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => commands.Exit());
        icon = new NotifyIcon
        {
            Icon = artwork,
            Text = (Build.IsDebug ? "Taskbar Runner (DEBUG)" : "Taskbar Runner") + " — 左クリックで遊ぶ / 右クリックでメニュー",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) commands.Play();
        };
    }

    /// <summary>Windows の通知でメッセージを表示する。error が true なら警告マークを付ける。</summary>
    internal void Notify(string message, bool error = false)
    {
        icon.BalloonTipTitle = "Taskbar Runner";
        icon.BalloonTipText = message;
        icon.BalloonTipIcon = error ? ToolTipIcon.Warning : ToolTipIcon.Info;
        icon.ShowBalloonTip(5000); // 5秒の表示を指定する。実際に何秒出るかは Windows の設定で変わる。
    }

    /// <summary>四角形を組み合わせ、ゲームのロボットと同じ見た目の32×32ピクセルのアイコンを描く。</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        // デバッグ版は胴体を別の色にして、通知領域に2つ並んでも一目で見分けられるようにする。
        using var mint = new SolidBrush(Build.IsDebug ? Color.FromArgb(255, 138, 128) : Color.FromArgb(141, 240, 198));
        using var dark = new SolidBrush(Color.FromArgb(21, 39, 49));
        using var orange = new SolidBrush(Color.FromArgb(255, 193, 131));
        g.Clear(Color.Transparent);
        g.FillRectangle(dark, 4, 6, 24, 21);    // 胴体の輪郭。
        g.FillRectangle(mint, 6, 7, 20, 18);    // 胴体の内側の色。
        g.FillRectangle(dark, 13, 11, 16, 8);   // 顔の部分。
        g.FillRectangle(Brushes.White, 16, 13, 3, 3); // 左目。
        g.FillRectangle(Brushes.White, 23, 13, 3, 3); // 右目。
        g.FillRectangle(orange, 11, 2, 5, 5);   // 頭のアンテナ。
        g.FillRectangle(mint, 6, 26, 7, 4);     // 左足。
        g.FillRectangle(mint, 21, 26, 7, 4);    // 右足。
        // 描いたロボットを Windows 用のアイコンにする。このアイコンは、使い終わったら DestroyIcon で削除する必要がある。
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        artwork.Dispose();
    }
}
