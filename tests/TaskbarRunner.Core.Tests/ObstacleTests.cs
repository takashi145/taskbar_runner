using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// 障害物の種類ごとに「どう避けるのが正解か」を固定する。
/// 高さや当たり判定の定数を少し変えるだけで、跳んで越せるはずの障害物が越せなくなったり、
/// しゃがむしかないはずの障害物を跳んで回避できてしまったりするので、その境目をここで押さえる。
/// </summary>
public sealed class ObstacleTests
{
    // TallBar は「1回のジャンプでは足りず、2段ジャンプなら越せる」高さであることが設計の肝。
    // 1回で越せたら簡単すぎ、2回でも越せなければ理不尽になる。その両側をまとめて確かめる。
    // 最後の確認は、跳び上がりすぎてタスクバー上の描画領域からはみ出さないこと。
    [Theory(DisplayName = "Tall bars need the second jump and remain below its apex at every FPS")]
    [MemberData(nameof(TallBarsNeedTheSecondJumpAndRemainBelowItsApexAtEveryFPSCases))]
    public void TallBarsNeedTheSecondJumpAndRemainBelowItsApexAtEveryFPS(int fps)
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"{fps} FPS");
        var game = Playing();
        var tall = new Obstacle(game.PlayerX, ObstacleKind.TallBar);
        game.Jump(); Advance(game, 4.0 / 15, fps);
        Assert.True(game.PlayerHitbox.Intersects(tall.Hitbox),
            $"A single jump must not clear a TallBar (height {game.HeightAboveGround:F2})");
        var height = game.HeightAboveGround;
        Assert.True(game.Jump()); Advance(game, .2, fps);
        Assert.Equal(GameSession.SecondJumpPower * .2 - .5 * GameSession.Gravity * .2 * .2, game.HeightAboveGround - height, .001);
        Assert.True(game.HeightAboveGround is > 100 and < 106,
            $"The second jump must reach just over a TallBar, was {game.HeightAboveGround:F2}");
        Assert.True(!game.PlayerHitbox.Intersects(tall.Hitbox),
            $"The second jump must clear a TallBar (height {game.HeightAboveGround:F2})");
        Assert.True(game.HeightAboveGround + GameSession.PlayerHeight < 180,
            $"The player must stay inside the playfield, reached {game.HeightAboveGround + GameSession.PlayerHeight:F2}");
    }

    // OverheadBar は「しゃがむしかない」障害物。2段ジャンプの2回目をいつ押すかで到達する高さが変わるので、
    // 押すタイミングを1フレームずつずらして総当たりし、どこかに跳び越せる隙間がないかを探す。
    // 1つでも抜けられる timing があると、しゃがみ操作そのものが不要になってしまう。
    [Fact(DisplayName = "Overhead bars block double jumps at every second-jump timing")]
    public void OverheadBarsBlockDoubleJumpsAtEverySecondJumpTiming()
    {
        foreach (var fps in new[] { 30, 60, 120 })
        for (var secondFrame = 1; secondFrame < fps / 2; secondFrame++)
        {
            TestContext.Current.TestOutputHelper!.WriteLine($"{fps} FPS, second jump on frame {secondFrame}");
            var game = Playing();
            var overhead = new Obstacle(game.PlayerX, ObstacleKind.OverheadBar);
            game.Jump();
            for (var frame = 0; frame < fps; frame++)
            {
                if (frame == secondFrame) Assert.True(game.Jump());
                game.Update(1.0 / fps);
                Assert.True(game.PlayerHitbox.Intersects(overhead.Hitbox),
                    $"Frame {frame} bypassed the OverheadBar at height {game.HeightAboveGround:F2}");
            }
            Assert.True(game.IsGrounded, $"Still airborne after 1s at height {game.HeightAboveGround:F2}");
            game.Duck(true);
            Assert.True(!game.PlayerHitbox.Intersects(overhead.Hitbox), "Ducking must clear the OverheadBar");
        }
    }

    // 当たり判定そのものの単体テスト。辺がぴったり接しただけ（10 と 10）では衝突にしない。
    // ここを「以上」にすると、かすってもいないのに当たったことになり、理不尽に感じる死に方が増える。
    [Fact(DisplayName = "Inset AABBs reject edge contact and detect overlap")]
    public void InsetAABBsRejectEdgeContactAndDetectOverlap()
    {
        var box = new Hitbox(0, 0, 10, 10);
        Assert.True(!box.Intersects(new Hitbox(10, 0, 5, 5)));
        Assert.True(box.Intersects(new Hitbox(9, 9, 5, 5)));
        Assert.True(!box.Intersects(new Hitbox(0, -6, 5, 5)));
    }

    // しゃがみが成立するための条件を、見た目と当たり判定の両面から確かめる。
    // Clearance（地面からの浮き）がしゃがみ時の背丈29より小さいと、判定では通れても絵が障害物にめり込む。
    // 最後の1行は逆向きの確認で、しゃがめば何でも避けられる（＝ずっとしゃがむのが最適）になっていないこと。
    [Fact(DisplayName = "OverheadBar collides with standing player but clears crouching sprite and hitbox")]
    public void OverheadBarCollidesWithStandingPlayerButClearsCrouchingSpriteAndHitbox()
    {
        var game = Playing();
        var overheadBar = new Obstacle(game.PlayerX, ObstacleKind.OverheadBar);
        Assert.True(game.PlayerHitbox.Intersects(overheadBar.Hitbox),
            $"A standing player must hit the OverheadBar (player {game.PlayerHitbox}, bar {overheadBar.Hitbox})");
        game.Duck(true);
        Assert.True(!game.PlayerHitbox.Intersects(overheadBar.Hitbox),
            $"A crouching player must pass under it (player {game.PlayerHitbox}, bar {overheadBar.Hitbox})");
        Assert.True(overheadBar.Clearance > 29, "Crouching sprite must fit below overheadBar artwork");
        Assert.True(game.PlayerHitbox.Intersects(new Obstacle(game.PlayerX).Hitbox), "Crouching must not bypass ground bars");
    }

    // 上の総当たりが2段ジャンプ側なのに対し、こちらは1回のジャンプの弧を1/240秒ずつ追う。
    // 上りきる前・頂点・落ちている途中のどの高さでも当たり続けることを確かめ、
    // 頂点の高さが物理の計算どおり（初速²÷2÷重力）になっていることも同時に押さえる。
    [Fact(DisplayName = "Overhead bar blocks every phase of the full jump arc")]
    public void OverheadBarBlocksEveryPhaseOfTheFullJumpArc()
    {
        var game = Playing();
        var overheadBar = new Obstacle(game.PlayerX, ObstacleKind.OverheadBar);
        game.Jump();
        var maximumHeight = 0.0;
        for (var i = 0; i < 192; i++)
        {
            game.Update(1.0 / 240);
            maximumHeight = Math.Max(maximumHeight, game.HeightAboveGround);
            Assert.True(game.PlayerHitbox.Intersects(overheadBar.Hitbox),
                $"Jump bypasses overheadBar at height {game.HeightAboveGround:F2}");
        }
        Assert.Equal(GameSession.JumpPower * GameSession.JumpPower / (2 * GameSession.Gravity), maximumHeight, .01);
        Assert.True(game.IsGrounded, $"The jump must have landed, height {game.HeightAboveGround:F2}");
        game.Duck(true);
        Assert.True(!game.PlayerHitbox.Intersects(overheadBar.Hitbox), "Ducking must still clear the barrier");
    }


    public static TheoryData<int> TallBarsNeedTheSecondJumpAndRemainBelowItsApexAtEveryFPSCases
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (var fps in new[] { 30, 60, 120 })
                data.Add(fps);
            return data;
        }
    }
}
