using System;
using System.Windows;
using TaskbarRunner.Core;

namespace TaskbarRunner;

/// <summary>
/// 設定画面。<see cref="GameSettings"/> の値に合う選択肢を表示し、選び直した内容を設定値に戻す。
/// 保存ボタンを押すと、画面を作るときに受け取った save を呼ぶ。結果が true なら保存できたとして画面を閉じる。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Func<GameSettings, bool> save;
    // この画面で変更しない設定も残せるよう、開いたときの設定を覚えておく。
    private readonly GameSettings original;
    // 数値は画面の選択肢と同じ順番に並べる。例えば「ゆっくり」を選ぶと、先頭の0.75を使う。
    private static readonly int[] FpsLimits = [30, 60];
    private static readonly double[] Speeds = [.75, 1, 1.25];
    private static readonly double[] Sizes = [.75, 1, 1.25, 1.5];

    public SettingsWindow(GameSettings settings, SaveData data, Func<GameSettings, bool> save)
    {
        InitializeComponent();
        settings = settings.Sanitize();
        original = settings;
        this.save = save;
        Fps.ItemsSource = new[] { "省電力 · 上限30 FPS", "標準 · 上限60 FPS" };
        Fps.SelectedIndex = Array.IndexOf(FpsLimits, settings.FpsLimit);
        Speed.ItemsSource = new[] { "ゆっくり · 0.75×", "標準 · 1.00×", "はやい · 1.25×" };
        // ファイルに選択肢と違う値が書かれていたら、一番近い値の項目を選んでおく。
        Speed.SelectedIndex = Nearest(Speeds, settings.GameSpeed);
        CharacterSize.ItemsSource = new[] { "小 · 75%", "標準 · 100%", "大 · 125%", "特大 · 150%" };
        CharacterSize.SelectedIndex = Nearest(Sizes, settings.CharacterScale);
        Offset.Value = settings.DisplayOffset;
        StartAtMaxSpeed.IsChecked = settings.StartAtMaxSpeed;
        Statistics.Text = $"BEST {data.BestScore:D5}    /    TOTAL RUNS {data.TotalRuns}";
    }

    // target に一番近い値が、配列の何番目にあるかを返す（先頭は0）。同じくらい近い値があれば、先にある方を選ぶ。
    private static int Nearest(double[] values, double target)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (Math.Abs(values[i] - target) < Math.Abs(values[best] - target)) best = i;
        return best;
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        var settings = original with
        {
            FpsLimit = FpsLimits[Fps.SelectedIndex],
            GameSpeed = Speeds[Speed.SelectedIndex],
            CharacterScale = Sizes[CharacterSize.SelectedIndex],
            DisplayOffset = (int)Offset.Value,
            StartAtMaxSpeed = StartAtMaxSpeed.IsChecked == true
        };
        if (save(settings)) DialogResult = true;
    }
}
