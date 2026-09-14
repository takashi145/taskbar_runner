namespace TaskbarRunner.Core;

/// <summary>次回の起動後も残しておく記録。最高得点、遊んだ回数、走った距離の合計。</summary>
public sealed record SaveData
{
    public int BestScore { get; init; }
    public long TotalRuns { get; init; }
    // ゲーム内で走った距離の合計。画面を拡大する前のピクセル数で数える。
    public double TotalDistance { get; init; }

    /// <summary>記録がマイナスなら0に直す。距離が通常の数として扱えない値になっていた場合も0に戻す。</summary>
    public SaveData Sanitize() => this with
    {
        BestScore = Math.Max(0, BestScore),
        TotalRuns = Math.Max(0, TotalRuns),
        TotalDistance = double.IsFinite(TotalDistance) ? Math.Max(0, TotalDistance) : 0
    };
}
