using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// キャラクターの動き方を確かめる。設定可能な30 / 60に加えて120回/秒の更新も検証し、
/// 更新頻度を変えても跳べる高さが変わらないことを確かめる。
/// 高さや距離は物理の式と突き合わせ、実装の計算結果をそのまま期待値にしない。
/// </summary>
public sealed class MovementTests
{
    // ジャンプの高さが、放物運動の式（初速×時間 − ½×重力×時間²）と一致することを確かめる。
    // 実装が1/240秒ずつ積み上げた結果と理論値がずれないので、積分の誤差が溜まっていないと分かる。
    [Fact(DisplayName = "Jump follows a ballistic arc and lands on ground")]
    public void JumpFollowsABallisticArcAndLandsOnGround()
    {
        var game = Playing();
        Assert.True(game.Jump());
        Advance(game, .25, 240);
        Assert.Equal(GameSession.JumpPower * .25 - .5 * GameSession.Gravity * .25 * .25, game.HeightAboveGround, .001);
        Assert.True(game.JumpsUsed == 1);
        Advance(game, .55, 240);
        Assert.Equal(0, game.HeightAboveGround, .001);
        Assert.True(game.IsGrounded && game.Jump());
    }

    // 2段目は1段目より弱いが、落下中に押しても上向きに切り替わる（速度を足すのではなく設定し直す）。
    // これがないと、落ち始めてから押した2段ジャンプがほとんど効かず、操作感が読めなくなる。
    // ジャンプ回数が戻るのは着地したときだけで、空中にいる限り何度押しても増えないことも確かめる。
    [Theory(DisplayName = "Second jump is weaker, reverses a fall, and only landing restores jumps")]
    [MemberData(nameof(SecondJumpIsWeakerReversesAFallAndOnlyLandingRestoresJumpsCases))]
    public void SecondJumpIsWeakerReversesAFallAndOnlyLandingRestoresJumps(int fps)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"{fps} FPS");
        var game = Playing();
        Assert.True(game.Jump()); Advance(game, .4, fps);
        var height = game.HeightAboveGround;
        Assert.True(game.Jump() && game.JumpsUsed == 2 && !game.Jump());
        Advance(game, .1, fps);
        Assert.Equal(GameSession.SecondJumpPower * .1 - .5 * GameSession.Gravity * .1 * .1, game.HeightAboveGround - height, .001);
        Assert.True(!game.Jump()); Advance(game, .6, fps);
        Assert.True(game.IsGrounded && game.JumpsUsed == 0 && game.Jump());
    }

    // しゃがむと当たり判定が縮み、しゃがんでいる間は跳べない。この「跳べない」が、
    // しゃがみと2段ジャンプを同時に使えないという制約になり、避け方の選択を意味のあるものにしている。
    [Fact(DisplayName = "Duck shrinks hitbox and release permits jumping")]
    public void DuckShrinksHitboxAndReleasePermitsJumping()
    {
        var game = Playing();
        var height = game.PlayerHitbox.Height;
        game.Duck(true);
        Assert.True(game.IsDucking && game.PlayerHitbox.Height < height && !game.Jump());
        game.Duck(false);
        Assert.True(game.PlayerHitbox.Height == height && game.Jump());
    }

    // 他のテストが各 FPS で個別に条件を満たすかを見るのに対し、これは3つの結果を直接突き合わせる。
    // FPS 設定が有利・不利に直結しないこと（=設定で難易度が変わらないこと）を保証する。
    [Fact(DisplayName = "30 / 60 / 120 FPS produce the same jump and distance")]
    public void FrameRates3060120FPSProduceTheSameJumpAndDistance()
    {
        var games = new[] { 30, 60, 120 }.Select(fps =>
        {
            var game = Playing(); game.Jump(); Advance(game, .5, fps); return game;
        }).ToArray();
        foreach (var game in games)
        {
            Assert.Equal(games[0].HeightAboveGround, game.HeightAboveGround, .001);
            Assert.Equal(games[0].Distance, game.Distance, .02);
        }
    }

    // 左右同時押しはその場で止まる（後から押した方を優先しない）。キーボードの押し順に依存すると、
    // 実装は単純でも遊ぶ側には挙動が読めなくなる。移動量が FPS に依存しないことも同時に確かめる。
    [Theory(DisplayName = "Horizontal movement is frame-independent and stops on release or opposing input")]
    [MemberData(nameof(HorizontalMovementIsFrameIndependentAndStopsOnReleaseOrOpposingInputCases))]
    public void HorizontalMovementIsFrameIndependentAndStopsOnReleaseOrOpposingInput(int fps)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"{fps} FPS");
        var game = Playing();
        var start = game.PlayerX;
        game.MoveRight(true); Advance(game, .2, fps);
        Assert.Equal(start + 44, game.PlayerX, .001);
        game.MoveLeft(true); Advance(game, .1, fps);
        Assert.Equal(start + 44, game.PlayerX, .001);
        game.MoveRight(false); Advance(game, .2, fps);
        Assert.Equal(start, game.PlayerX, .001);
        game.MoveLeft(false); Advance(game, .1, fps);
        Assert.Equal(start, game.PlayerX, .001);
        Assert.Equal(game.PlayerX + 7, game.PlayerHitbox.X, .001);
        Assert.True(game.Score == 6 || game.Score == 5); // 小数の計算でわずかに6点を下回ることがあるので、切り捨て後の5点でも合格とする。
    }

    // キャラクターは画面左寄りの範囲だけを動く。右に行けすぎると障害物が見えてから避けるまでが
    // 短くなりすぎ、左に行けすぎると画面外に出る。横幅320（最小）から3440（横長）まで確認し、
    // 最後に画面が狭くなったときも Configure が位置を引き戻すことを見ている。
    [Theory(DisplayName = "Movement stays within the left side at every supported speed and viewport")]
    [MemberData(nameof(MovementStaysWithinTheLeftSideAtEverySupportedSpeedAndViewportCases))]
    public void MovementStaysWithinTheLeftSideAtEverySupportedSpeedAndViewport(int viewport, double multiplier)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"width {viewport}, {multiplier}x");
        var game = NewGame(); game.Configure(viewport, multiplier); game.Ready(); game.Start();
        Assert.True(game.HorizontalSpeed > 0 && game.HorizontalSpeed < game.Speed);
        Assert.True(game.MaximumPlayerX + GameSession.PlayerWidth <= viewport * .45);
        game.MoveRight(true);
        for (var i = 0; i < 120; i++) { Avoid(game, true); game.Update(1.0 / 60); }
        Assert.True(game.State == GameState.Playing, $"Collided while moving right: {Snapshot(game)}");
        Assert.Equal(game.MaximumPlayerX, game.PlayerX, .001);
        game.MoveRight(false); game.MoveLeft(true);
        for (var i = 0; i < 180; i++) { Avoid(game, true); game.Update(1.0 / 60); }
        Assert.True(game.State == GameState.Playing, $"Collided while moving left: {Snapshot(game)}");
        Assert.Equal(game.MinimumPlayerX, game.PlayerX, .001);
        game.Configure(320, multiplier);
        Assert.True(game.PlayerX >= 0 && game.PlayerX + GameSession.PlayerWidth < 320);
    }

    // 空中でもしゃがみ中でも左右に動ける。ただし横に動いたことで跳ぶ高さや進んだ距離、
    // 得点が変わってはいけない。動かないキャラクターと並べて走らせ、縦の計算が独立していることを確かめる。
    [Fact(DisplayName = "Jumping and crouching allow horizontal movement without changing vertical physics")]
    public void JumpingAndCrouchingAllowHorizontalMovementWithoutChangingVerticalPhysics()
    {
        var game = Playing(); var stationary = Playing();
        var start = game.PlayerX;
        game.Jump(); stationary.Jump(); game.MoveRight(true);
        Advance(game, .2, 60); Advance(stationary, .2, 60);
        Assert.True(game.PlayerX > start && !game.IsGrounded);
        Assert.Equal(stationary.HeightAboveGround, game.HeightAboveGround, .001);
        Assert.Equal(stationary.Distance, game.Distance, .001);
        Assert.True(game.Score == stationary.Score);
        game.MoveRight(false); game.Duck(true); Advance(game, .4, 60);
        Assert.True(game.IsDucking && game.IsGrounded);
        start = game.PlayerX;
        game.MoveLeft(true); Advance(game, .1, 60);
        Assert.True(game.PlayerX < start && game.IsDucking);
    }

    // 右に寄るほど障害物に早く当たり、左に逃げるほど長く生き延びる。当たり判定がキャラクターの
    // 横位置に追従している証拠になる。同時に「左に逃げ続ければ永久に avoid できる」抜け道がないこと
    // （左端で止まるので、いずれ必ず追いつかれる）も確かめている。
    [Fact(DisplayName = "Collision follows horizontal position and moving left cannot escape obstacles")]
    public void CollisionFollowsHorizontalPositionAndMovingLeftCannotEscapeObstacles()
    {
        var times = new List<double>();
        foreach (var direction in new[] { 1, 0, -1 })
        {
            TestContext.Current.TestOutputHelper!.WriteLine($"direction {direction}");
            var game = Playing();
            game.MoveLeft(direction < 0); game.MoveRight(direction > 0);
            Advance(game, 15, 30);
            Assert.True(game.State == GameState.GameOver, $"Survived 15s: {Snapshot(game)}");
            Assert.True(game.Obstacles.Any(o => o.Hitbox.Intersects(game.PlayerHitbox)), Snapshot(game));
            times.Add(game.Elapsed);
        }
        Assert.True(times[0] < times[1] && times[1] < times[2],
            $"Moving right must end a run sooner than standing still, and left latest: {string.Join(", ", times.Select(t => $"{t:F2}s"))}");
    }

    // 空中で下キーを押しておくと、着地した瞬間にしゃがむ。地上に降りてから押し直す必要があると、
    // 「跳んで越えた直後にしゃがんで避ける」という組み合わせが人間の反応速度では間に合わなくなる。
    [Fact(DisplayName = "Holding Down during flight crouches immediately on landing")]
    public void HoldingDownDuringFlightCrouchesImmediatelyOnLanding()
    {
        var game = Playing();
        game.Jump(); Advance(game, .1, 60); game.Duck(true);
        Assert.True(!game.IsDucking);
        Advance(game, .7, 60);
        Assert.True(game.IsGrounded && game.IsDucking && !game.Jump());
        game.Duck(false);
        Assert.True(!game.IsDucking && game.Jump());
    }

    // 上の予約の裏返し。着地前に指を離したら予約は取り消す。押した事実だけを覚える実装だと、
    // 気が変わって離したのに着地時に勝手にしゃがみ、そのまま跳べなくなる。
    [Fact(DisplayName = "Releasing Down before landing cancels the queued crouch")]
    public void ReleasingDownBeforeLandingCancelsTheQueuedCrouch()
    {
        var game = Playing();
        game.Jump(); Advance(game, .1, 60); game.Duck(true); game.Duck(false);
        Advance(game, .7, 60);
        Assert.True(game.IsGrounded && !game.IsDucking);
    }


    public static TheoryData<int> SecondJumpIsWeakerReversesAFallAndOnlyLandingRestoresJumpsCases
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var fps in new[] { 30, 60, 120 })
                data.Add(fps);
            return data;
        }
    }

    public static TheoryData<int> HorizontalMovementIsFrameIndependentAndStopsOnReleaseOrOpposingInputCases
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var fps in new[] { 30, 60, 120 })
                data.Add(fps);
            return data;
        }
    }

    public static TheoryData<int, double> MovementStaysWithinTheLeftSideAtEverySupportedSpeedAndViewportCases
    {
        get
        {
            var data = new TheoryData<int, double>();
            foreach (var viewport in new[] { 320, 1280, 3440 })
            foreach (var multiplier in new[] { .75, 1.0, 1.25 })
                data.Add(viewport, multiplier);
            return data;
        }
    }
}
