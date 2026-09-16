using TaskbarRunner.Core;

namespace TaskbarRunner.Tests;

/// <summary>テストからゲームを動かすための共通処理。障害物の並びが毎回同じになるよう、既定では決まった種を使う。</summary>
internal static class GameTestSupport
{
    /// <summary>テスト用の乱数の種。この値を変えると、障害物の並びに依存するテストの結果も変わる。</summary>
    internal const int DefaultSeed = 42;

    /// <summary>まだ始めていないゲームを作る。同じ種を使うので、何度実行しても同じ障害物が出る。</summary>
    internal static GameSession NewGame(int seed = DefaultSeed) => new(random: new Random(seed));

    /// <summary>すぐ遊べる状態のゲームを作る。</summary>
    internal static GameSession Playing(int seed = DefaultSeed)
    {
        var game = NewGame(seed);
        game.Ready();
        game.Start();
        return game;
    }

    /// <summary>指定した秒数ぶん、1秒あたり fps 回に分けてゲームを進める。</summary>
    internal static void Advance(GameSession game, double seconds, int fps)
    {
        var frames = (int)Math.Round(seconds * fps);
        for (var frame = 0; frame < frames; frame++) game.Update(1.0 / fps);
    }

    /// <summary>次の障害物を避けるようにキー操作を決める。避けられるはずの障害物かどうかを確かめるときに使う。</summary>
    internal static void Avoid(GameSession game, bool duckOverheadBars = true) =>
        AvoidancePilot.Steer(game, duckOverheadBars);

    /// <summary>失敗したときに状況を読み取れるよう、そのときのゲームの様子を1行にまとめる。ぶつかっている障害物には * を付ける。</summary>
    internal static string Snapshot(GameSession game)
    {
        var player = game.PlayerHitbox;
        var obstacles = string.Join(", ", game.Obstacles.Take(4).Select(obstacle =>
            $"{obstacle.Kind}@{obstacle.X:F1}+{obstacle.Width:F0}{(obstacle.Hitbox.Intersects(player) ? "*" : "")}"));
        return $"state={game.State}, t={game.Elapsed:F2}s, x={game.PlayerX:F1}, height={game.HeightAboveGround:F1}, " +
            $"jumps={game.JumpsUsed}, ducking={game.IsDucking}, speed={game.Speed:F1}, obstacles=[{obstacles}]";
    }

    /// <summary>空のフォルダーを用意してテストを実行し、終わったら中身ごと消す。</summary>
    internal static void WithTemp(Action<string> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "TaskbarRunnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            body(folder);
        }
        finally
        {
            // 後片付けに失敗しても、テストの結果を上書きしない。
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
