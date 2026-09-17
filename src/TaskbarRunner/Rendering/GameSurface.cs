using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TaskbarRunner.Core;

namespace TaskbarRunner.Rendering;

/// <summary>キャラクター、障害物、得点を1つの画面にまとめて描く。</summary>
internal sealed class GameSurface(GameSession game) : FrameworkElement
{
    private static readonly SolidColorBrush Ink = Brush("#152731");          // 輪郭に使う濃い色。
    private static readonly SolidColorBrush Mint = Brush("#8DF0C6");         // ロボットの本体色。
    private static readonly SolidColorBrush MintShade = Brush("#4CAB8B");    // ロボットの影。
    private static readonly SolidColorBrush Orange = Brush("#FFC183");       // 地上の障害物と、ゲームオーバー時のロボット。
    private static readonly SolidColorBrush Violet = Brush("#CAB0FF");       // 空中の障害物。
    private static readonly SolidColorBrush OrangeLight = Brush("#FFE3BC");  // 障害物の上側のつや。
    private static readonly SolidColorBrush OrangeShade = Brush("#D69656");  // 障害物の下側の影。
    private static readonly SolidColorBrush VioletLight = Brush("#E6D7FF");
    private static readonly SolidColorBrush VioletShade = Brush("#9674C7");
    private static readonly SolidColorBrush GroundStripes = Brush("#75533B");   // 地上の障害物の斜線模様。
    private static readonly SolidColorBrush OverheadStripes = Brush("#6D538D"); // 空中の障害物の斜線模様。
    // 同じ幅の障害物には同じ斜線模様を使えるので、作った模様を保存しておく。
    private static readonly Dictionary<double, Geometry> StripeShapes = [];
    private static readonly SolidColorBrush White = Brush("#F1F8F5");
    private static readonly SolidColorBrush Muted = Brush("#AFBCC4");        // 見出しなどに使う薄い文字色。
    private static readonly SolidColorBrush Panel = Brush("#F21A252F");      // 最初の F2 は色の濃さ。約5%だけ背景が透ける。
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface Mono = new("Consolas");  // 得点には、どの数字も同じ幅になる字体を使って桁をそろえる。
    /// <summary>ロボットと障害物を何倍の大きさで描くか。文字の大きさは変えない。</summary>
    internal double CharacterScale { get; set; } = 1;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (game.State == GameState.Idle || ActualWidth <= 0) return;
        var ground = ActualHeight; // この画面の一番下を地面にする。ゲーム内では地面が0、上に行くほどマイナス。
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        // 描く位置の基準を地面に合わせ、設定の倍率で拡大する。これでゲーム内の位置をそのまま使って描ける。
        dc.PushTransform(new TranslateTransform(0, ground));
        dc.PushTransform(new ScaleTransform(CharacterScale, CharacterScale));
        DrawRobot(dc, game.PlayerX, -game.HeightAboveGround);
        foreach (var obstacle in game.Obstacles)
            DrawBar(dc, obstacle);
        dc.Pop(); // 拡大を戻す。
        dc.Pop(); // 位置の基準を元に戻す。ここからの得点や操作説明は、画面の左上を基準に描く。

        var panelWidth = Math.Min(460, ActualWidth - 24);
        var x = (ActualWidth - panelWidth) / 2;
        var y = 14.0;
        dc.DrawRoundedRectangle(Panel, null, new Rect(x, y, panelWidth, 48), 12, 12);
        Text(dc, "BEST", x + 20, y + 8, 10, Muted);
        Text(dc, game.BestScore.ToString("D5", CultureInfo.InvariantCulture), x + 20, y + 23, 15, Mint, true);
        Text(dc, Build.IsDebug ? "TASKBAR RUNNER · DEBUG" : "TASKBAR RUNNER", x + panelWidth / 2, y + 8, 12, White, centered: true);
        Text(dc, $"SPEED {game.Speed / GameSession.InitialSpeed:0.0}×", x + panelWidth / 2,
            y + 27, 10, Mint, centered: true);
        Text(dc, "SCORE", x + panelWidth - 80, y + 8, 10, Muted);
        Text(dc, game.Score.ToString("D5", CultureInfo.InvariantCulture), x + panelWidth - 80, y + 23, 15, White, true);

        if (game.State is GameState.Ready or GameState.Paused or GameState.GameOver)
        {
            var (headline, headlineColor, detail, hint) = game.State switch
            {
                GameState.Ready => ("ひと息、ひとっ走り。", Mint, "SPACE でスタート",
                    "← → 移動 / SPACE でジャンプ・空中でもう一度 / ↓ しゃがむ"),
                GameState.Paused => ("一時停止中", White, $"SCORE {game.Score:D5}   ·   SPACE で再開",
                    "R で最初から / ESC で作業に戻る（走行は残ります）"),
                _ => ("GAME OVER", Orange, $"SCORE {game.Score:D5}   ·   SPACE でもう一度", "ESC で作業に戻る")
            };
            var boxWidth = Math.Min(440, panelWidth);
            var boxX = (ActualWidth - boxWidth) / 2;
            dc.DrawRoundedRectangle(Panel, null, new Rect(boxX, 72, boxWidth, 84), 12, 12);
            Text(dc, headline, ActualWidth / 2, 82, 19, headlineColor, centered: true);
            Text(dc, detail, ActualWidth / 2, 111, 12, White, centered: true);
            Text(dc, hint, ActualWidth / 2, 134, 10, Muted, centered: true);
        }
        else
        {
            Text(dc, "← → 移動    SPACE / ↑ 2段ジャンプ    ↓ しゃがむ    ESC 戻る",
                ActualWidth / 2, 71, 11, White, centered: true);
        }
        dc.Pop();
    }

    // x はキャラクターの左端、bottom は足元の位置。地面が0で、跳んでいる間の足元はマイナスになる。
    private void DrawRobot(DrawingContext dc, double x, double bottom)
    {
        var duck = game.IsDucking;
        var top = bottom - game.CurrentPlayerHeight; // 頭の位置。しゃがむと足元はそのままで頭だけ下がる。
        var dead = game.State == GameState.GameOver;
        // 走っている間は1秒に12回、足を前後に入れ替える。跳んでいる間としゃがんでいる間は足を動かさない。
        var stride = game.State == GameState.Playing && game.IsGrounded && !duck
            ? ((int)(game.Elapsed * 12) % 2 == 0 ? 3 : -3) : 0;
        Rect(dc, Ink, x + 6, top + 7, 34, duck ? 18 : 34);
        Rect(dc, dead ? Orange : Mint, x + 8, top + 6, 30, duck ? 16 : 30);
        if (!duck)
        {
            Rect(dc, MintShade, x + 10, top + 36, 24, 8);  // 胴体の下の影。
            Rect(dc, Ink, x + 17, top + 1, 3, 6);          // アンテナを支える棒。
            Rect(dc, Orange, x + 15, top, 7, 4);           // アンテナの先端。
        }
        Rect(dc, Ink, x + 18, top + 12, 23, 12); // 顔の窓。
        if (dead)
        {
            // ゲームオーバーでは目を×印にする。
            var pen = new Pen(Orange, 2);
            dc.DrawLine(pen, new Point(x + 24, top + 15), new Point(x + 30, top + 21));
            dc.DrawLine(pen, new Point(x + 30, top + 15), new Point(x + 24, top + 21));
        }
        else
        {
            Rect(dc, White, x + 24, top + 15, 4, 5);
            Rect(dc, White, x + 34, top + 15, 4, 5);
        }
        // 左足を前に出すときは右足を後ろに引く。stride に逆のプラス・マイナスを使って、走る動きに見せる。
        Rect(dc, Ink, x + 10 + stride, bottom - 8, 9, 8);
        Rect(dc, Ink, x + 29 - stride, bottom - 8, 9, 8);
        Rect(dc, Mint, x + 9 + stride, bottom - 4, 12, 4);
        Rect(dc, Mint, x + 28 - stride, bottom - 4, 12, 4);
        Rect(dc, MintShade, x + 2, top + (duck ? 13 : 27), 8, 7); // 背中側の腕。しゃがむと位置を上げる。
    }

    private static void DrawBar(DrawingContext dc, Obstacle obstacle)
    {
        // 障害物の一番上の位置。空中の障害物は、地面との隙間（Clearance）のぶんも上にずらす。
        var top = -obstacle.Clearance - obstacle.Height;
        var overhead = obstacle.Kind == ObstacleKind.OverheadBar;
        // しゃがんで通る障害物は紫、跳び越える障害物はオレンジにして、色で避け方が分かるようにする。
        var color = overhead ? Violet : Orange;
        Rect(dc, Ink, obstacle.X, top, obstacle.Width, obstacle.Height);                        // 輪郭。
        Rect(dc, color, obstacle.X + 2, top + 2, obstacle.Width - 4, obstacle.Height - 4);      // 縁の内側の色。
        Rect(dc, overhead ? VioletLight : OrangeLight, obstacle.X + 3, top + 3, obstacle.Width - 6, 2); // 上のつや。
        Rect(dc, overhead ? VioletShade : OrangeShade, obstacle.X + 3,
            top + obstacle.Height - 5, obstacle.Width - 6, 3);                                  // 下の影。
        if (obstacle.Kind == ObstacleKind.TallBar)
        {
            // 高い障害物には中央に線を入れ、棒を2本積んだ見た目にする。
            Rect(dc, Ink, obstacle.X + 2, top + obstacle.Height / 2, obstacle.Width - 4, 3);
            Rect(dc, OrangeLight, obstacle.X + 3, top + obstacle.Height / 2 + 3, obstacle.Width - 6, 2);
        }
        if (obstacle.Kind == ObstacleKind.WideBar)
        {
            // 幅広の障害物には、目印として小さな矢印を2つ描く。
            var pen = new Pen(GroundStripes, 2);
            var center = obstacle.X + obstacle.Width / 2;
            for (var i = 0; i < 2; i++)
            {
                var arrowX = center - 10 + i * 12;
                dc.DrawLine(pen, new Point(arrowX, top + 8), new Point(arrowX + 5, top + 12));
                dc.DrawLine(pen, new Point(arrowX + 5, top + 12), new Point(arrowX, top + 16));
            }
        }
        // 保存しておいた斜線模様を、障害物の下の方に合わせて描く。
        dc.PushTransform(new TranslateTransform(obstacle.X, top + obstacle.Height - 15));
        dc.DrawGeometry(overhead ? OverheadStripes : GroundStripes, null, StripesForWidth(obstacle.Width));
        dc.Pop();
    }

    private static Geometry StripesForWidth(double width)
    {
        if (StripeShapes.TryGetValue(width, out var cached)) return cached;
        // この幅の模様がまだなければ作る。画面上の位置は含めず、描くときに障害物の位置へ合わせる。
        var stripes = new StreamGeometry();
        using (var context = stripes.Open())
        {
            // 12ピクセルおきに斜めの帯を並べる。左端にも模様が入るよう、少し外側の-12から描き始める。
            for (var x = -12; x < width; x += 12)
            {
                context.BeginFigure(new Point(x, 9), isFilled: true, isClosed: true);
                context.LineTo(new Point(x + 5, 9), isStroked: false, isSmoothJoin: false);
                context.LineTo(new Point(x + 14, 0), isStroked: false, isSmoothJoin: false);
                context.LineTo(new Point(x + 9, 0), isStroked: false, isSmoothJoin: false);
            }
        }
        // 障害物から模様がはみ出さないよう、内側に収まる部分だけ残す。
        var clipped = Geometry.Combine(stripes, new RectangleGeometry(new Rect(2, 0, width - 4, 9)),
            GeometryCombineMode.Intersect, null);
        clipped.Freeze();
        StripeShapes.Add(width, clipped);
        return clipped;
    }

    // 文字を描く。通常は x が文字列の左端。centered が true なら、文字列の中央が x に来るようにする。
    private void Text(DrawingContext dc, string value, double x, double y, double size,
        Brush brush, bool mono = false, bool centered = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            mono ? Mono : Font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(centered ? x - text.Width / 2 : x, y));
    }

    private static void Rect(DrawingContext dc, Brush brush, double x, double y, double w, double h) =>
        dc.DrawRectangle(brush, null, new Rect(x, y, w, h));

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
