using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// 「出てくる障害物は必ず避けられるか」を、自動操縦（<see cref="AvoidancePilot"/>）に4分間遊ばせて確かめる。
/// 個々の障害物が避けられることと、実際のプレイで避け続けられることは別問題で、
/// 速さが上がった終盤や画面が狭い環境では、間隔が足りずに理不尽な組み合わせが生まれうる。
/// FPS・画面幅・速度倍率・乱数の種を総当たりし、詰む配置が1つも無いことを保証する。
/// </summary>
public sealed class AvoidanceTests
{
    // 左端・右端に貼り付いたまま4分間遊ぶ。端はどちらも不利になりやすく、
    // 右端は障害物が見えてから避けるまでが短く、左端は障害物が届くまでの時間が最も長い。
    // どちらの端に居続けても最高速度までクリアできることを確かめる。
    [Theory(DisplayName = "Both movement boundaries remain playable through maximum speed")]
    [MemberData(nameof(BothMovementBoundariesRemainPlayableThroughMaximumSpeedCases))]
    public void BothMovementBoundariesRemainPlayableThroughMaximumSpeed(int fps, int viewport, double multiplier, bool left)
    {
        var game = NewGame(); game.Configure(viewport, multiplier); game.Ready(); game.Start();
        game.MoveLeft(left); game.MoveRight(!left);
        for (var i = 0; i < fps * 240; i++)
        {
            Avoid(game, true); game.Update(1.0 / fps);
            Assert.True(game.State == GameState.Playing,
                $"Boundary collision at {game.Elapsed:F2}s, {fps} FPS, width {viewport}, {multiplier}x, left {left}: {Snapshot(game)}");
        }
    }

    // 本命のクリア可能性テスト。4種類すべての障害物が混ざった状態で最高速度まで避け続けられること、
    // 画面外に出た障害物が捨てられていること（放置するとリストが増え続ける）、
    // そして地上の障害物が3つ以上続く場面が実際に出ること＝種類が機械的に交互になっていないことを見る。
    [Theory(DisplayName = "Mixed obstacles remain avoidable through maximum speed")]
    [MemberData(nameof(MixedObstaclesRemainAvoidableThroughMaximumSpeedCases))]
    public void MixedObstaclesRemainAvoidableThroughMaximumSpeed(int fps, double multiplier, int viewport, int seed)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"{fps} FPS, {multiplier}x, width {viewport}, seed {seed}");
        var game = new GameSession(random: new Random(seed));
        game.Configure(viewport, multiplier);
        game.Ready(); game.Start();
        var seen = new HashSet<Obstacle>();
        var sequence = new List<ObstacleKind>();
        for (var i = 0; i < fps * 240; i++)
        {
            Avoid(game, duckOverheadBars: true);
            game.Update(1.0 / fps);
            Assert.True(game.State == GameState.Playing, $"Bot collision at {game.Elapsed:F2}s, {fps} FPS, {multiplier}x, width {viewport}, seed {seed}: {Snapshot(game)}");
            Assert.True(game.Obstacles.Count < 25 && game.Obstacles.All(o => o.X + o.Width >= 0),
                $"Off-screen obstacle leak: {Snapshot(game)}");
            foreach (var obstacle in game.Obstacles)
                if (seen.Add(obstacle)) sequence.Add(obstacle.Kind);
        }
        Assert.Equal(game.ScrollSpeedLimit, game.Speed, .001);
        Assert.True(Enum.GetValues<ObstacleKind>().All(sequence.Contains),
            $"Every obstacle kind must appear in a 4-minute run, saw [{string.Join(", ", sequence.Distinct())}]");
        Assert.True(sequence.Skip(2).Where((kind, i) => kind == ObstacleKind.GroundBar &&
            kind == sequence[i] && kind == sequence[i + 1]).Any(),
            $"Ground bars must still form runs rather than a fixed alternating pattern: {sequence.Count} obstacles");
    }

    // WideBar（幅広）は「2段目をわざと遅らせて滞空を伸ばす」ことを要求する障害物。
    // 跳んですぐ2段目を押す（early）と越えられず、遅らせる（late）と越えられる、という差が
    // どの速度・画面幅でも保たれることを確かめる。これが崩れると、押すタイミングを工夫する意味が消える。
    [Theory(DisplayName = "Wide bars reward a delayed second jump from the opening through maximum speed")]
    [MemberData(nameof(WideBarsRewardADelayedSecondJumpFromTheOpeningThroughMaximumSpeedCases))]
    public void WideBarsRewardADelayedSecondJumpFromTheOpeningThroughMaximumSpeed(int fps, int viewport, double multiplier, int after)
    {
        VerifyWideBar(fps, viewport, multiplier, after, "single");
        VerifyWideBar(fps, viewport, multiplier, after, "early");
        VerifyWideBar(fps, viewport, multiplier, after, "late");
        VerifyWideBar(fps, viewport, multiplier, after, "late", .45);
        VerifyWideBar(fps, viewport, multiplier, after, "right-late");
    }

    // 地上の障害物のすぐ後ろに空中の障害物が続く組み合わせ。ここでの正解は「1回だけ跳んですぐ着地し、
    // しゃがんで2つ目をくぐる」こと。2段目を押してしまうと滞空が伸びて空中の障害物に当たる。
    // つまり「跳ばない判断」に意味がある場面が成立しているかを確かめている。
    [Theory(DisplayName = "Tight ground-to-overhead pairs reward single jumps across screen sizes and speeds")]
    [MemberData(nameof(TightGroundToOverheadPairsRewardSingleJumpsAcrossScreenSizesAndSpeedsCases))]
    public void TightGroundToOverheadPairsRewardSingleJumpsAcrossScreenSizesAndSpeeds(int fps, int viewport, double multiplier)
    {
        VerifyFollowUpPair(fps, viewport, multiplier, null);
        VerifyFollowUpPair(fps, viewport, multiplier, .2);
        VerifyFollowUpPair(fps, viewport, multiplier, 4.0 / 15);
    }

    // 上の3つが「避けられること」を確かめるのに対し、これは逆に「避けられないこと」を確かめる。
    // しゃがみだけを使わずに遊ぶと必ず空中の障害物で終わる＝しゃがみ操作が省略できないと示す。
    [Fact(DisplayName = "Ignoring crouch causes collision with the first overheadBar")]
    public void IgnoringCrouchCausesCollisionWithTheFirstOverheadBar()
    {
        var game = Playing();
        for (var i = 0; i < 60 * 12 && game.State == GameState.Playing; i++)
        {
            Avoid(game, duckOverheadBars: false);
            game.Update(1.0 / 60);
        }
        Assert.True(game.State == GameState.GameOver, $"Never crouching must end the run: {Snapshot(game)}");
        Assert.True(game.Obstacles.Any(o => o.Kind == ObstacleKind.OverheadBar && o.Hitbox.Intersects(game.PlayerHitbox)),
            $"The run must end on an OverheadBar, not another obstacle: {Snapshot(game)}");
    }

    // 上と同じ主張を、より厳しく確かめる。ObstacleTests は静止した障害物で調べているが、
    // 実際の障害物は近づいてくるので、跳ぶ位置によって当たり方が変わる。
    // 手前0.05秒から0.65秒まで踏み切り位置をずらして総当たりし、跳んで抜けられる隙間がないことを見る。
    [Theory(DisplayName = "Jump timing cannot bypass a moving overhead bar")]
    [MemberData(nameof(JumpTimingCannotBypassAMovingOverheadBarCases))]
    public void JumpTimingCannotBypassAMovingOverheadBar(int fps, double multiplier, double leadTime)
    {
        var game = Playing();
        game.Configure(1280, multiplier);
        for (var i = 0; i < fps * 15 && game.State == GameState.Playing; i++)
        {
            var next = game.Obstacles.FirstOrDefault(o => o.X + o.Width > game.PlayerX + 7);
            if (next?.Kind == ObstacleKind.OverheadBar)
            {
                game.Duck(false);
                if (next.X - (game.PlayerX + GameSession.PlayerWidth) < game.Speed * leadTime)
                    game.Jump();
            }
            else Avoid(game, true);
            game.Update(1.0 / fps);
        }
        Assert.True(game.State == GameState.GameOver && game.Obstacles.Any(o =>
            o.Kind == ObstacleKind.OverheadBar && o.Hitbox.Intersects(game.PlayerHitbox)),
            $"OverheadBar avoided with jump: {fps} FPS, {multiplier}x, lead {leadTime}s");
    }



    /// <summary>
    /// 地上→空中の密な組み合わせが実際に出るまで遊び、そこを1回のジャンプで抜けられるかを確かめる。
    /// secondJumpDelay が null なら2段目を押さずに抜けられること、値があればその秒数後に
    /// 2段目を押すと必ず空中の障害物に当たることを期待する（＝跳びすぎが罰せられる）。
    /// </summary>
    static void VerifyFollowUpPair(int fps, int viewport, double multiplier, double? secondJumpDelay)
    {
        var game = NewGame(); game.Configure(viewport, multiplier); game.Ready(); game.Start();
        Obstacle? target = null;
        Obstacle? followUp = null;
        Obstacle? jumpedOver = null;
        var jumpedAt = 0.0;
        var usedSecondJump = false;
        var context = $"{fps} FPS, width {viewport}, {multiplier}x, second at {secondJumpDelay}";
        for (var frame = 0; frame < fps * 120; frame++)
        {
            var upcoming = game.Obstacles.Where(o => o.X + o.Width > game.PlayerX + 7).Take(2).ToArray();
            var next = upcoming.FirstOrDefault();
            if (target is null && upcoming.Length == 2 && next!.Kind == ObstacleKind.GroundBar &&
                upcoming[1].Kind == ObstacleKind.OverheadBar && (upcoming[1].X - next.X) / game.Speed < .55)
            {
                target = next;
                followUp = upcoming[1];
            }
            var jumpsBefore = game.JumpsUsed;
            Avoid(game, true);
            if (jumpsBefore == 0 && game.JumpsUsed == 1)
            {
                jumpedOver = next;
                jumpedAt = game.Elapsed;
            }
            if (target is not null && jumpedOver == target && secondJumpDelay.HasValue &&
                game.JumpsUsed == 1 && game.Elapsed - jumpedAt >= secondJumpDelay.Value - 1e-9)
            {
                Assert.True(game.Jump(), context);
                usedSecondJump = true;
            }
            game.Update(1.0 / fps);
            if (game.State == GameState.GameOver)
            {
                Assert.True(usedSecondJump && followUp is not null && game.PlayerHitbox.Intersects(followUp.Hitbox),
                    $"Single-jump route failed or double jump hit the wrong obstacle: {context}; {Snapshot(game)}");
                return;
            }
            if (followUp is not null && followUp.X + followUp.Width < game.PlayerX + 7)
            {
                Assert.True(!secondJumpDelay.HasValue, $"Extra double jump bypassed the pair: {context}");
                return;
            }
        }
        Assert.Fail($"No tight pair encountered: {context}");
    }
    /// <summary>
    /// 幅広の障害物を、指定した攻略法（route）で越えられるかを確かめる。
    /// single は1回だけ跳ぶ、early は早すぎる2段目、late は遅らせた2段目、right-late は右に動きながら遅らせる。
    /// single と early は当たり、late と right-late は越えられるのが期待どおりの結果。
    /// 跳ぶ直前には、滞空時間と障害物の幅から「理屈の上でも越えられる」ことを先に確認しておく。
    /// </summary>
    static void VerifyWideBar(int fps, int viewport, double multiplier, int after, string route, double lateDelay = .40)
    {
        var game = NewGame(); game.Configure(viewport, multiplier); game.Ready(); game.Start();
        game.MoveLeft(true);
        Obstacle? target = null;
        double? jumpedAt = null;
        var context = $"{fps} FPS, width {viewport}, {multiplier}x, after {after}s, {route}, late delay {lateDelay}";
        for (var frame = 0; frame < fps * (after + 90); frame++)
        {
            var next = game.Obstacles.FirstOrDefault(o => o.X + o.Width > game.PlayerX + 7);
            if (target is null && game.Elapsed >= after && game.IsGrounded && next?.Kind == ObstacleKind.WideBar)
                target = next;
            var relativeSpeed = game.Speed + (route == "right-late" ? game.HorizontalSpeed : 0);
            if (target is not null && !jumpedAt.HasValue && game.IsGrounded &&
                target.X - (game.PlayerX + GameSession.PlayerWidth) < relativeSpeed * .10)
            {
                game.MoveLeft(false); game.MoveRight(route == "right-late"); game.Duck(false);
                var requiredHeight = game.PlayerHitbox.Y + game.PlayerHitbox.Height - target.Hitbox.Y;
                var timeAboveBar = 2 * Math.Sqrt(GameSession.JumpPower * GameSession.JumpPower -
                    2 * GameSession.Gravity * requiredHeight) / GameSession.Gravity;
                var jumpDuration = 2 * GameSession.JumpPower / GameSession.Gravity;
                var speedDuringJump = Math.Min(game.ScrollSpeedLimit,
                    game.Speed + GameSession.Acceleration * multiplier * jumpDuration);
                Assert.True(target.Hitbox.Width + game.PlayerHitbox.Width > speedDuringJump * timeAboveBar,
                    $"A single jump has enough clearance time for the whole width: {context}");
                Assert.True(game.Jump(), context);
                jumpedAt = game.Elapsed;
            }
            if (!jumpedAt.HasValue)
            {
                // 調べる障害物を決めたら、このテストで決めた位置までジャンプせずに待つ。
                if (target is null) Avoid(game, true);
            }
            else if (route != "single" && game.JumpsUsed == 1 &&
                game.Elapsed - jumpedAt.Value >= (route == "early" ? .20 : lateDelay) - 1e-9)
            {
                Assert.True(game.Jump(), context);
            }
            game.Update(1.0 / fps);
            if (game.State == GameState.GameOver)
            {
                Assert.True(route is ("single" or "early") && jumpedAt.HasValue && target is not null && game.PlayerHitbox.Intersects(target.Hitbox),
                    $"Wide route failed: {context}; {Snapshot(game)}");
                return;
            }
            if (jumpedAt.HasValue && target!.X + target.Width < game.PlayerX + 7)
            {
                Assert.True(route is "late" or "right-late", $"A short jump bypassed a wide bar: {context}");
                Assert.True(game.JumpsUsed == 2, context);
                return;
            }
        }
        Assert.Fail($"No wide bar encountered: {context}");
    }
    public static TheoryData<int, int, double, bool> BothMovementBoundariesRemainPlayableThroughMaximumSpeedCases
    {
        get
        {
            var data = new TheoryData<int, int, double, bool>();
            foreach (var fps in new[] { 30, 60, 120 })
            foreach (var viewport in new[] { 320, 1280, 3440 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
            foreach (var left in new[] { true, false })
                data.Add(fps, viewport, multiplier, left);
            return data;
        }
    }

    public static TheoryData<int, double, int, int> MixedObstaclesRemainAvoidableThroughMaximumSpeedCases
    {
        get
        {
            var data = new TheoryData<int, double, int, int>();
            foreach (var fps in new[] { 30, 60, 120 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
            foreach (var viewport in new[] { 320, 1280, 3440 })
            foreach (var seed in new[] { 0, 42, 137 })
                data.Add(fps, multiplier, viewport, seed);
            return data;
        }
    }

    public static TheoryData<int, int, double, int> WideBarsRewardADelayedSecondJumpFromTheOpeningThroughMaximumSpeedCases
    {
        get
        {
            var data = new TheoryData<int, int, double, int>();
            foreach (var fps in new[] { 30, 60, 120 })
            foreach (var viewport in new[] { 320, 1280, 3440 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
            foreach (var after in new[] { 12, 100, 225 })
                data.Add(fps, viewport, multiplier, after);
            return data;
        }
    }

    public static TheoryData<int, int, double> TightGroundToOverheadPairsRewardSingleJumpsAcrossScreenSizesAndSpeedsCases
    {
        get
        {
            var data = new TheoryData<int, int, double>();
            foreach (var fps in new[] { 30, 60, 120 })
            foreach (var viewport in new[] { 320, 1280, 3440 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
                data.Add(fps, viewport, multiplier);
            return data;
        }
    }

    public static TheoryData<int, double, double> JumpTimingCannotBypassAMovingOverheadBarCases
    {
        get
        {
            var data = new TheoryData<int, double, double>();
            foreach (var fps in new[] { 30, 60, 120 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
            foreach (var leadTime in new[] { .05, .15, .25, .35, .45, .55, .65 })
                data.Add(fps, multiplier, leadTime);
            return data;
        }
    }
}
