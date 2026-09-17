using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskbarRunner;
using TaskbarRunner.Core;
using TaskbarRunner.Platform;
using TaskbarRunner.Rendering;
using TaskbarRunner.Tests;
using Xunit;

// WPFの描画、ネイティブ配置、クリック透過とフォーカスの結合テスト。
[Collection("Desktop")]
[Trait("Category", "Desktop")]
public sealed class RenderingTests(DesktopFixture desktop)
{
    [Fact(Skip = "Set TASKBARRUNNER_DESKTOP_TESTS=1 to run desktop tests",
        SkipUnless = nameof(DesktopFixture.IsEnabled), SkipType = typeof(DesktopFixture))]
    public Task GameStatesRenderAndOverlayPreservesInputStyles() => desktop.RunAsync(RenderAndInteract);

    private static void RenderAndInteract()
    {
        var output = Path.GetFullPath("artifacts/screenshots");
        Directory.CreateDirectory(output);
        var game = new GameSession(random: new Random(42));
        var overlay = new OverlayWindow(game);
        try
        {
            Assert.True(!overlay.IsVisible, "Overlay starts hidden");
            AssertIdleStyles(overlay);
            // 何も描かれていない画像がどう見えるかを先に測っておき、以降の「絵が入っている」判定の基準にする。
            var blank = Render(new GameSurface(new GameSession()), 1280, 280, Path.Combine(output, "idle-blank.png"));
            Assert.True(blank.DrawnPixels == 0, $"An idle session draws nothing, so a blank render is detectable ({blank})");
            game.Ready();
            var ready = RenderVisible(new GameSurface(game), 1280, 280, Path.Combine(output, "ready.png"));
            game.Start();
            for (var i = 0; i < 150; i++) { AvoidancePilot.Steer(game); game.Update(1.0 / 60); }
            game.Jump();
            game.MoveRight(true);
            for (var i = 0; i < 12; i++) { AvoidancePilot.Steer(game); game.Update(1.0 / 60); }
            game.MoveRight(false);
            Assert.True(game.State == GameState.Playing, "Playing snapshot is an active run");
            var playing = RenderVisible(new GameSurface(game), 1280, 280, Path.Combine(output, "playing.png"));
            // 一時停止は画面を隠したまま進む状態なので、開き直したときに止まっていると分かる必要がある。
            game.Pause();
            var paused = RenderVisible(new GameSurface(game), 1280, 280, Path.Combine(output, "paused.png"));
            game.Resume();
            for (var i = 0; i < 600; i++) game.Update(1.0 / 60);
            Assert.True(game.State == GameState.GameOver, "Game over snapshot follows collision");
            var gameOver = RenderVisible(new GameSurface(game), 1280, 280, Path.Combine(output, "game-over.png"));
            Assert.True(new[] { ready, playing, paused, gameOver }.Select(stats => stats.Fingerprint).Distinct().Count() == 4,
                $"Ready / playing / paused / game over each draw something different (ready {ready.Fingerprint}, " +
                $"playing {playing.Fingerprint}, paused {paused.Fingerprint}, game over {gameOver.Fingerprint})");
            var duckingGame = new GameSession(random: new Random(42));
            duckingGame.Ready(); duckingGame.Start();
            var capturedOverheadBar = false;
            var capturedDoubleJump = false;
            var capturedFollowUp = false;
            var capturedWideBar = false;
            for (var i = 0; i < 3600 && duckingGame.State == GameState.Playing; i++)
            {
                var next = duckingGame.Obstacles.FirstOrDefault(o => o.X + o.Width > duckingGame.PlayerX + 7);
                if (next is not null && next.X - duckingGame.PlayerX < 180)
                {
                    if (!capturedWideBar && next.Kind == ObstacleKind.WideBar &&
                        duckingGame.JumpsUsed == 2 && duckingGame.HeightAboveGround > 60)
                    {
                        RenderVisible(new GameSurface(duckingGame), 1280, 280, Path.Combine(output, "wide-bar.png"));
                        capturedWideBar = true;
                    }
                    var following = duckingGame.Obstacles.SkipWhile(o => o != next).Skip(1).FirstOrDefault();
                    if (!capturedFollowUp && next.Kind == ObstacleKind.GroundBar &&
                        following?.Kind == ObstacleKind.OverheadBar &&
                        (following.X - next.X) / duckingGame.Speed < .55)
                    {
                        RenderVisible(new GameSurface(duckingGame), 1280, 280, Path.Combine(output, "follow-up-pair.png"));
                        capturedFollowUp = true;
                    }
                    if (!capturedOverheadBar && next.Kind == ObstacleKind.OverheadBar && duckingGame.IsGrounded)
                    {
                        // 同じ場面を、しゃがむ前と後で1枚ずつ描く。絵が変わることまで確かめないと、
                        // しゃがみが描画に反映されていなくても画像は出てしまう。
                        var standing = RenderVisible(new GameSurface(duckingGame), 1280, 280,
                            Path.Combine(output, "standing.png"));
                        duckingGame.Duck(true);
                        var ducking = RenderVisible(new GameSurface(duckingGame), 1280, 280,
                            Path.Combine(output, "ducking.png"));
                        Assert.True(standing.Fingerprint != ducking.Fingerprint,
                            $"Crouching changes what is drawn (standing {standing}, ducking {ducking})");
                        Assert.True(ducking.DrawnPixels < standing.DrawnPixels,
                            $"The crouching robot covers less of the screen than the standing one " +
                            $"({ducking.DrawnPixels} vs {standing.DrawnPixels} pixels)");
                        capturedOverheadBar = true;
                    }
                    if (!capturedDoubleJump && next.Kind == ObstacleKind.TallBar &&
                        duckingGame.JumpsUsed == 2 && duckingGame.HeightAboveGround > 95)
                    {
                        RenderVisible(new GameSurface(duckingGame), 1280, 280, Path.Combine(output, "double-jump.png"));
                        capturedDoubleJump = true;
                    }
                }
                if (capturedOverheadBar && capturedDoubleJump && capturedFollowUp && capturedWideBar) break;
                AvoidancePilot.Steer(duckingGame);
                duckingGame.Update(1.0 / 60);
            }
            // ここで確かめられるのは「その場面に到達し、空でない絵が描けた」ことまで。
            // 描かれた絵が意図どおりの見た目かどうかは、artifacts/screenshots の画像を人が見て判断する。
            Assert.True(capturedOverheadBar, "Reached an OverheadBar while grounded and captured the crouch");
            Assert.True(capturedDoubleJump, "Reached a TallBar during the weaker second jump and captured it");
            Assert.True(capturedFollowUp, "Reached a tight ground-to-overhead pair and captured it");
            Assert.True(capturedWideBar, "Reached a wide bar during the delayed second jump and captured it");
            var settingsWindow = new SettingsWindow(new GameSettings(), new SaveData { BestScore = 123, TotalRuns = 4 }, _ => true);
            var content = (FrameworkElement)settingsWindow.Content;
            // 設定画面を画像にするときも同じ見た目になるよう、元のウィンドウの色や字体を引き継ぐ。
            content.Resources = settingsWindow.Resources;
            TextElement.SetForeground(content, settingsWindow.Foreground);
            TextElement.SetFontFamily(content, settingsWindow.FontFamily);
            TextElement.SetFontSize(content, settingsWindow.FontSize);
            settingsWindow.Content = null;
            RenderVisible(new Border { Background = settingsWindow.Background, Child = content },
                480, 600, Path.Combine(output, "settings.png"));
            settingsWindow.Close();
            Console.WriteLine("INFO WPF rendering checked only for non-blank, state-dependent output. " +
                $"Review the appearance by hand: {output}");

            if (TaskbarLocator.TryLocate(out var placement, out var error))
            {
                overlay.Position(placement, new GameSettings());
                Pump();
                NativeMethods.GetWindowRect(overlay.Handle, out var rect);
                Assert.True(rect.Bottom == placement.Top && rect.Right - rect.Left == placement.Width,
                    "Native overlay is exactly above the taskbar");
                Console.WriteLine($"Taskbar: ({placement.Left}, {placement.Top}), width={placement.Width}, DPI={placement.DpiScale * 96}");
                {
                    game.Ready();
                    Assert.True(overlay.BeginInteraction(), "Play acquires foreground focus");
                    Pump();
                    NativeMethods.GetWindowRect(overlay.Handle, out rect);
                    Assert.True(rect.Bottom == placement.Top && rect.Right - rect.Left == placement.Width,
                        "Showing WPF window preserves native placement");
                    var style = NativeMethods.GetWindowLongPtr(overlay.Handle, NativeMethods.GwlExStyle).ToInt64();
                    Assert.True((style & (NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate)) == 0,
                        "Active overlay accepts local input");
                    var lostFocus = false;
                    overlay.FocusLost += () => { lostFocus = true; overlay.EndInteraction(); };
                    var other = new Window { Title = "Taskbar Runner focus test", Width = 240, Height = 100 };
                    try
                    {
                        other.Show(); other.Activate(); Pump();
                        Assert.True(lostFocus && !overlay.IsVisible, "Another window returns overlay to hidden idle");
                    }
                    finally { other.Close(); }
                    AssertIdleStyles(overlay);
                }
            }
            else
            {
                throw new InvalidOperationException(error);
            }
            Console.WriteLine($"Windows smoke checks passed. Renders to review by hand: {output}");
        }
        finally { overlay.Close(); }
    }

    private static void AssertIdleStyles(OverlayWindow overlay)
    {
        var style = NativeMethods.GetWindowLongPtr(overlay.Handle, NativeMethods.GwlExStyle).ToInt64();
        Assert.True((style & NativeMethods.WsExTransparent) != 0 && (style & NativeMethods.WsExNoActivate) != 0 &&
              (style & NativeMethods.WsExToolWindow) != 0 && (style & NativeMethods.WsExAppWindow) == 0,
            "Idle is click-through, non-activating, and excluded from taskbar / Alt+Tab");
    }

    /// <summary>描いた絵の中身。画像を書き出せたかどうかとは別に、何がどれだけ描かれたかを数えたもの。</summary>
    private readonly record struct RenderStats(int DrawnPixels, int TotalPixels, string Fingerprint)
    {
        internal double DrawnRatio => (double)DrawnPixels / TotalPixels;
        public override string ToString() => $"{DrawnRatio:P1} drawn, fingerprint {Fingerprint}";
    }

    // 画像を書き出し、そのうち何ピクセルが実際に塗られたかと、中身を表す短い値を返す。
    // これだけでは「見た目が正しい」ことまでは分からない。空の画像や、状態を変えても同じ絵になる不具合を見つけるために使う。
    private static RenderStats Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path)) encoder.Save(stream);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        image.CopyPixels(pixels, stride, 0);
        var drawn = 0;
        // Pbgra32 の4バイト目が透明度。ほぼ透明なままの点は「描かれていない」と数える。
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 8) drawn++;
        return new RenderStats(drawn, width * height, Convert.ToHexString(SHA256.HashData(pixels))[..16]);
    }

    // 空でない絵が描けたことまで確かめてから返す。
    private static RenderStats RenderVisible(FrameworkElement element, int width, int height, string path)
    {
        var stats = Render(element, width, height, path);
        Assert.True(stats.DrawnRatio > .005,
            $"{Path.GetFileName(path)} contains a drawn image, not a blank one ({stats})");
        return stats;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

}
