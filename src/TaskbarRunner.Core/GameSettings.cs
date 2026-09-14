namespace TaskbarRunner.Core;

/// <summary>設定ファイルに保存する内容。変更するときは with でコピーを作り、元の設定は残しておく。</summary>
public sealed record GameSettings
{
    public int FpsLimit { get; init; } = 60;
    // 速さと大きさは1が標準。表示位置をずらす量は、Windows の表示倍率が100%のときのピクセル数で指定する。
    public double GameSpeed { get; init; } = 1;
    public double CharacterScale { get; init; } = 1;
    public int DisplayOffset { get; init; }

    /// <summary>設定ファイルに使えない値があっても動くように直す。大きすぎる値や小さすぎる値は範囲内に収め、通常の数として扱えない値は標準に戻す。</summary>
    public GameSettings Sanitize() => this with
    {
        FpsLimit = FpsLimit is 30 or 60 ? FpsLimit : 60,
        GameSpeed = double.IsFinite(GameSpeed) ? Math.Clamp(GameSpeed, .75, 1.25) : 1,
        CharacterScale = double.IsFinite(CharacterScale) ? Math.Clamp(CharacterScale, .75, 1.5) : 1,
        DisplayOffset = Math.Clamp(DisplayOffset, 0, 60)
    };
}
