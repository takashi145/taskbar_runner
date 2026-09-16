using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// 障害物の出方と速さの上がり方を確かめる。ここだけは1回のプレイを見ても判断できないので、
/// 20種類の乱数の種で4分ずつ遊ばせ、種類・並び・間隔に関する性質を見る。
/// 出現率や間隔の平均は調整時の参考として出力するが、現在の調整値を合否の基準にしない。
/// </summary>
public sealed class SpawnTests
{
    // しゃがみを要求する障害物は連続させない。連続すると下キーを
    // 押しっぱなしにするだけで抜けられてしまい、ジャンプと使い分ける意味がなくなる。
    // 幅広の障害物の幅が整数であることも確かめる。描画側が幅をキーに模様をキャッシュしているので、
    // 小数が混じるとキャッシュが際限なく増える。
    [Fact(DisplayName = "All obstacle kinds appear without consecutive crouch or double-jump obstacles")]
    public void AllObstacleKindsAppearWithoutConsecutiveCrouchOrDoubleJumpObstacles()
    {
        var total = 0;
        var overhead = 0;
        var tightPairs = 0;
        var wideBars = 0;
        var kinds = new HashSet<ObstacleKind>();
        for (var seed = 0; seed < 20; seed++)
        {
            TestContext.Current.TestOutputHelper!.WriteLine($"seed {seed}");
            var game = new GameSession(random: new Random(seed));
            game.Ready(); game.Start();
            var seen = new HashSet<Obstacle>();
            ObstacleKind? previous = null;
            Obstacle? previousObstacle = null;
            for (var i = 0; i < 60 * 240; i++)
            {
                Avoid(game, true); game.Update(1.0 / 60);
                Assert.True(game.State == GameState.Playing, $"The bot collided: {Snapshot(game)}");
                foreach (var obstacle in game.Obstacles)
                {
                    if (!seen.Add(obstacle)) continue;
                    total++;
                    kinds.Add(obstacle.Kind);
                    if (obstacle.Kind == ObstacleKind.TallBar)
                    {
                        Assert.True(previous is not (ObstacleKind.TallBar or ObstacleKind.WideBar),
                            $"TallBar came right after {previous} at {game.Elapsed:F2}s");
                    }
                    if (obstacle.Kind == ObstacleKind.WideBar)
                    {
                        Assert.True(previous is not (ObstacleKind.TallBar or ObstacleKind.WideBar),
                            $"WideBar came right after {previous} at {game.Elapsed:F2}s");
                        Assert.True(double.IsFinite(obstacle.Width) && obstacle.Width > 0 && obstacle.Width == Math.Ceiling(obstacle.Width),
                            $"WideBar width must be a positive whole number, was {obstacle.Width} at {game.Elapsed:F2}s");
                        wideBars++;
                    }
                    if (obstacle.Kind == ObstacleKind.OverheadBar)
                    {
                        Assert.True(previous != ObstacleKind.OverheadBar,
                            $"Consecutive obstacles allow holding Down indefinitely (at {game.Elapsed:F2}s)");
                        overhead++;
                        if (previousObstacle?.Kind == ObstacleKind.GroundBar &&
                            (obstacle.X - previousObstacle.X) / game.Speed < .55)
                            tightPairs++;
                    }
                    previous = obstacle.Kind;
                    previousObstacle = obstacle;
                }
            }
        }
        var rate = (double)overhead / total;
        Assert.True(kinds.SetEquals(Enum.GetValues<ObstacleKind>()),
            $"Some obstacle kinds never appeared: observed [{string.Join(", ", kinds)}]");
        TestContext.Current.TestOutputHelper!.WriteLine($"INFO Crouching obstacles: {overhead}/{total} ({rate:P1})");
        var pairRate = (double)tightPairs / total;
        TestContext.Current.TestOutputHelper!.WriteLine($"INFO Tight pairs: {tightPairs}/{total} ({pairRate:P1})");
        TestContext.Current.TestOutputHelper!.WriteLine($"INFO Wide bars: {wideBars}/{total} ({(double)wideBars / total:P1})");
    }

    // 出だしが毎回同じだと、覚えゲーになって繰り返し遊ぶ意味が薄れる。
    // 32回遊んで最初の障害物の種類と幅が固定されていないことを確かめる。
    [Fact(DisplayName = "Openings and widths change between runs")]
    public void OpeningsAndWidthsChangeBetweenRuns()
    {
        var game = NewGame();
        var openings = new HashSet<ObstacleKind>();
        var widths = new HashSet<double>();
        for (var run = 0; run < 32; run++)
        {
            TestContext.Current.TestOutputHelper!.WriteLine($"run {run}");
            game.Ready(); game.Start();
            Advance(game, 1, 60);
            Assert.True(game.Obstacles.Count > 0, $"No obstacle appeared in the first second: {Snapshot(game)}");
            openings.Add(game.Obstacles[0].Kind);
            widths.Add(game.Obstacles[0].Width);
        }
        Assert.True(openings.Count > 1 && widths.Count > 1,
            $"Runs repeat a fixed opening: kinds [{string.Join(", ", openings)}], {widths.Count} distinct widths");
    }

    // 種を固定すれば完全に同じ並びを再現できる。これが成り立つから、他のテストが
    // 「この種で落ちた」という形で不具合を再現でき、失敗を追いかけられる。
    // 逆に種が違えば並びも変わる（＝種を受け取っても無視していない）ことも確かめる。
    [Fact(DisplayName = "Seeded runs are reproducible while different seeds vary the pattern")]
    public void SeededRunsAreReproducibleWhileDifferentSeedsVaryThePattern()
    {
        Assert.True(Trace(42) == Trace(42), "Same seed must reproduce a run for debugging");
        Assert.True(Trace(42) != Trace(137), "Different seeds must vary kinds, widths, or timing");
    }

    // 到達までの検証時間は現在の加速度から決める。途中の速度や何秒で難しくなるかは固定しない。
    // 上限到達後も1秒進め、上限を超えないことと、再開で初期状態に戻ることを確かめる。
    [Theory(DisplayName = "Speed increases to its configured cap and resets on retry")]
    [MemberData(nameof(SpeedIncreasesToItsConfiguredCapAndResetsOnRetryCases))]
    public void SpeedIncreasesToItsConfiguredCapAndResetsOnRetry(double multiplier)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"speed multiplier {multiplier}");
        var game = Playing();
        game.Configure(1280, multiplier);
        var initial = game.Speed;
        var cap = game.ScrollSpeedLimit;
        Assert.True(GameSession.Acceleration > 0 && double.IsFinite(GameSession.Acceleration));
        var secondsToCap = (cap - initial) / (GameSession.Acceleration * multiplier);
        for (var i = 0; i < Math.Ceiling((secondsToCap + 1) * 60); i++)
        {
            var previousSpeed = game.Speed;
            var previousDifficulty = game.DifficultyProgress;
            Avoid(game, true); game.Update(1.0 / 60);
            Assert.True(game.State == GameState.Playing, $"The bot collided: {Snapshot(game)}");
            Assert.True(double.IsFinite(game.Speed) && game.Speed >= previousSpeed && game.Speed <= cap,
                $"Speed must not decrease or exceed {cap:F3}: {previousSpeed:F3} to {game.Speed:F3} at {game.Elapsed:F2}s");
            Assert.True(double.IsFinite(game.DifficultyProgress) && game.DifficultyProgress >= previousDifficulty && game.DifficultyProgress <= 1,
                $"Difficulty must increase within [0, 1], was {game.DifficultyProgress:R}");
        }
        Assert.True(Math.Abs(game.Speed - (cap)) <= .001, "Acceleration must eventually reach the configured speed cap");
        Assert.True(game.DifficultyProgress > 0, "Difficulty must progress during an active run");
        game.Ready(); game.Start();
        Assert.Equal(initial, game.Speed, .001);
        Assert.True(game.DifficultyProgress == 0, $"Retry must reset difficulty, was {game.DifficultyProgress:F3}");
    }

    // 障害物の間隔のリズムを見る。間隔が一定だと単調になり、長い空白があると手持ち無沙汰になる。
    // 正の間隔で繰り返し出現し、その間隔に変化があることを確認する。
    // 序盤と後半の平均や最大間隔は、遊びごたえを調整するための参考値として出力する。
    [Fact(DisplayName = "Spawning continues with positive and varied intervals")]
    public void SpawningContinuesWithPositiveAndVariedIntervals()
    {
        // たまたま障害物が続いただけで判断しないよう、何度も遊んだ結果を比べて、障害物が出る頻度を確認する。
        var intervals = new List<(double Start, double Gap)>();
        for (var seed = 0; seed < 20; seed++)
        {
            TestContext.Current.TestOutputHelper!.WriteLine($"seed {seed}");
            var game = new GameSession(random: new Random(seed));
            game.Ready(); game.Start();
            var seen = new HashSet<Obstacle>();
            var times = new List<double>();
            for (var i = 0; i < 60 * 240; i++)
            {
                Avoid(game, true); game.Update(1.0 / 60);
                Assert.True(game.State == GameState.Playing, $"The bot collided: {Snapshot(game)}");
                foreach (var obstacle in game.Obstacles)
                    if (seen.Add(obstacle)) times.Add(game.Elapsed);
            }
            var runIntervals = times.Zip(times.Skip(1), (a, b) => (Start: a, Gap: b - a)).ToArray();
            Assert.True(times.Any(time => time < 120) && times.Any(time => time >= 120),
                "Obstacles must continue spawning in both halves of the run");
            Assert.True(runIntervals.All(x => double.IsFinite(x.Gap) && x.Gap > 0),
                "Consecutive obstacles must have a finite, positive spawn interval");
            intervals.AddRange(runIntervals);
        }
        Assert.True(intervals.Select(x => Math.Round(x.Gap, 2)).Distinct().Count() > 1,
            $"Obstacle spacing is too regular: only {intervals.Select(x => Math.Round(x.Gap, 2)).Distinct().Count()} distinct gaps");
        var early = intervals.Where(x => x.Start < 40).Average(x => x.Gap);
        var late = intervals.Where(x => x.Start > 205).Average(x => x.Gap);
        TestContext.Current.TestOutputHelper!.WriteLine($"INFO Spawn intervals: mean {intervals.Average(x => x.Gap):F3}s, max {intervals.Max(x => x.Gap):F3}s, early {early:F3}s, late {late:F3}s");
    }



    // 1回のプレイの出だし25個ぶんを「種類:幅:時刻」の文字列にして、並びをそのまま比較できるようにする。
    static string Trace(int seed)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"trace seed {seed}");
        var game = new GameSession(random: new Random(seed));
        game.Ready(); game.Start();
        var seen = new HashSet<Obstacle>();
        var trace = new List<string>();
        for (var i = 0; i < 3600 && trace.Count < 25; i++)
        {
            Avoid(game, true); game.Update(1.0 / 60);
            Assert.True(game.State == GameState.Playing, $"The bot collided: {Snapshot(game)}");
            foreach (var obstacle in game.Obstacles)
                if (seen.Add(obstacle)) trace.Add($"{obstacle.Kind}:{obstacle.Width}:{game.Elapsed:F3}");
        }
        Assert.True(trace.Count == 25, $"Only {trace.Count} obstacles appeared in 60s");
        return string.Join(";", trace);
    }
    public static TheoryData<double> SpeedIncreasesToItsConfiguredCapAndResetsOnRetryCases
    {
        get
        {
            var data = new TheoryData<double>();
            foreach (var multiplier in new[] { .75, 1, 1.25 })
                data.Add(multiplier);
            return data;
        }
    }
}
