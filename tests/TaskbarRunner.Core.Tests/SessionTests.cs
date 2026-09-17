using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// Idle・Ready・Playing・GameOver の行き来を確かめる。
/// 作業中のタスクバーに常駐するゲームなので、遊んでいないときに勝手に動いたり、
/// キー入力を横取りしたりしないことが、遊びやすさより先に守るべき性質になる。
/// </summary>
public sealed class SessionTests
{
    // 遊んでいないときは、画面を描く処理から Update が呼ばれても何も起きてはいけない。
    // 得点が増えたり障害物が出たりすると、次に遊び始めたときに途中から始まってしまう。
    [Fact(DisplayName = "Idle ignores simulation and game input")]
    public void IdleIgnoresSimulationAndGameInput()
    {
        var game = NewGame();
        game.Start(); game.Update(10); game.Duck(true);
        Assert.True(game.State == GameState.Idle && game.Score == 0 && game.Obstacles.Count == 0,
            $"Idle must ignore Start and Update: {Snapshot(game)}");
        Assert.True(!game.Jump() && !game.IsDucking, "Idle must ignore jump and crouch input");
    }

    // SPACE を押す前の待機中も時間を進めない。押した瞬間から遊び始められるようにする。
    // 逆に Ready に戻したときは、前回の得点や障害物が残っていてはいけない。
    [Fact(DisplayName = "Ready does not simulate; start resets the run")]
    public void ReadyDoesNotSimulateStartResetsTheRun()
    {
        var game = NewGame();
        game.Ready(); game.Update(2);
        Assert.True(game.State == GameState.Ready && game.Elapsed == 0);
        game.Start(); game.Update(.1);
        Assert.True(game.State == GameState.Playing && game.Elapsed > 0);
        game.Ready();
        Assert.True(game.Score == 0 && game.Obstacles.Count == 0 && game.IsGrounded);
    }

    // ジャンプ回数は Update ではなく Jump の呼び出しで数える。そうしないと、
    // 1フレームの間に SPACE を連打しただけで3回目以降も跳べてしまい、障害物を素通りできる。
    // あわせて、中断・やり直し・ゲームオーバーのそれぞれで回数が正しく戻ることも確かめる。
    [Fact(DisplayName = "Rapid jump presses cannot create a third jump before the first frame")]
    public void RapidJumpPressesCannotCreateAThirdJumpBeforeTheFirstFrame()
    {
        var game = Playing();
        Assert.True(game.Jump() && game.Jump() && !game.Jump());
        game.Update(1.0 / 60);
        Assert.True(!game.IsGrounded && game.JumpsUsed == 2 && !game.Jump());
        game.Stop(); Assert.True(!game.Jump());
        game.Ready(); Assert.True(game.JumpsUsed == 0 && !game.Jump());
        game.Start(); Assert.True(game.Jump() && game.Jump());
        Advance(game, 10, 60); Assert.True(game.State == GameState.GameOver && !game.Jump());
        game.Start(); Assert.True(game.JumpsUsed == 0 && game.Jump());
    }

    // RunFinished はアプリ側で記録をファイルに保存するきっかけになる。ぶつかった後も Update が
    // 呼ばれ続けるので、ここが2回以上呼ばれるとプレイ回数が水増しされ、保存も無駄に走る。
    [Fact(DisplayName = "Collision ends exactly one run and retry clears obstacles")]
    public void CollisionEndsExactlyOneRunAndRetryClearsObstacles()
    {
        var game = Playing();
        var finished = 0;
        game.RunFinished += () => finished++;
        Advance(game, 10, 60);
        Assert.True(game.State == GameState.GameOver && finished == 1 && game.Score > 0,
            $"One collision must finish exactly one run: {Snapshot(game)}, RunFinished raised {finished} times, score {game.Score}");
        var best = game.BestScore;
        Assert.True(best == game.Score, $"Best score {best} must match the finished run's {game.Score}");
        game.Update(1); game.Stop();
        Assert.True(finished == 1, $"Stopping after GameOver must not finish again, raised {finished} times");
        game.Ready(); game.Start();
        Assert.True(game.State == GameState.Playing && game.Score == 0 && game.Obstacles.Count == 0 && game.BestScore == best,
            $"Retry must clear the run but keep the best score {best}: {Snapshot(game)}, best {game.BestScore}");
    }

    // ゲームオーバー画面から SPACE だけでやり直せる。間に Ready を挟まないと遊べない作りだと、
    // 何度も続けて遊ぶときに手数が増えてしまう。
    [Fact(DisplayName = "Space retry can start directly from GameOver")]
    public void SpaceRetryCanStartDirectlyFromGameOver()
    {
        var game = Playing();
        Advance(game, 10, 60);
        game.Start();
        Assert.True(game.State == GameState.Playing && game.Score == 0 && game.IsGrounded);
    }

    // ESC や他のアプリへの切り替えで中断したとき。Stop は複数の経路から呼ばれるので
    // （ESC・フォーカス喪失・アプリ終了）、重なって呼ばれても保存は1回に抑える必要がある。
    // 中断後は時間が進まないことも確かめ、裏で得点が伸び続けないようにする。
    [Fact(DisplayName = "Focus loss / escape stops simulation and saves once")]
    public void FocusLossEscapeStopsSimulationAndSavesOnce()
    {
        var game = Playing();
        var finished = 0;
        game.RunFinished += () => finished++;
        Advance(game, .5, 60);
        game.Duck(true); game.Stop(); game.Stop();
        var elapsed = game.Elapsed;
        game.Update(1);
        Assert.True(game.State == GameState.Idle && !game.IsDucking && finished == 1 && game.Elapsed == elapsed,
            $"Stopping twice must idle, release the crouch, and save once: {Snapshot(game)}, " +
            $"RunFinished raised {finished} times, elapsed {game.Elapsed:F3} vs {elapsed:F3}");
    }

    // 別のアプリに切り替わると、キーを離したことがゲームに届かない。押しっぱなしの扱いが残ると、
    // 次に遊び始めた瞬間に勝手に走り出す。プレイ中以外は左右キーを受け付けず、
    // 新しく遊び始めるたびに押している向きが消えることを確かめる。
    [Fact(DisplayName = "Inactive states ignore movement and each new run clears held directions")]
    public void InactiveStatesIgnoreMovementAndEachNewRunClearsHeldDirections()
    {
        var game = NewGame();
        game.MoveRight(true); game.Update(.1);
        game.Ready(); var start = game.PlayerX;
        game.MoveLeft(true); game.Update(.1); Assert.Equal(start, game.PlayerX, .001);
        game.Start(); game.Update(.1); Assert.Equal(start, game.PlayerX, .001);
        game.MoveRight(true); game.Update(.1); Assert.True(game.PlayerX > start);
        game.Stop(); var stopped = game.PlayerX; game.Update(.1); Assert.Equal(stopped, game.PlayerX, .001);
        game.Ready(); game.Start(); game.Update(.1); Assert.Equal(start, game.PlayerX, .001);
        game.MoveLeft(true); Advance(game, 15, 60);
        Assert.True(game.State == GameState.GameOver);
        var dead = game.PlayerX;
        game.MoveRight(true); game.Update(.1); Assert.Equal(dead, game.PlayerX, .001);
        game.Start(); game.Update(.1); Assert.Equal(start, game.PlayerX, .001);
    }

    // フレームの間隔は実測値なので、異常な値が入りうる。時計の巻き戻りで負の値、
    // 0除算で NaN や無限大になった場合に、位置が飛んだり計算が壊れたりしてはいけない。
    // 重い処理でアプリが止まった後も、進めるのは0.1秒までに抑えて「瞬間移動して即死」を防ぐ。
    [Fact(DisplayName = "Invalid and stalled frame durations are bounded")]
    public void InvalidAndStalledFrameDurationsAreBounded()
    {
        var game = Playing();
        foreach (var dt in new[] { double.NaN, double.PositiveInfinity, -1, 0 }) game.Update(dt);
        Assert.True(game.Elapsed == 0, $"Invalid frame durations must not advance the run, elapsed {game.Elapsed:R}");
        game.Update(100);
        Assert.Equal(.1, game.Elapsed, .00001);
    }

    // 空中で下キーを押すと「着地したらしゃがむ」予約になる。この予約が中断をまたいで残ると、
    // 次に遊び始めた直後から勝手にしゃがみ、しゃがみ中は跳べないので動けなくなる。
    [Fact(DisplayName = "Focus loss clears a crouch queued while airborne")]
    public void FocusLossClearsACrouchQueuedWhileAirborne()
    {
        var game = Playing();
        game.Jump(); Advance(game, .1, 60); game.Duck(true); game.Stop();
        game.Ready(); game.Start(); Advance(game, .8, 60);
        Assert.True(!game.IsDucking && game.Jump());
    }

    // 画面幅と速さはタスクバーの実測値や設定ファイルから来るので、壊れた値が入りうる。
    // 黙って既定値に直すのではなく例外にして、呼び出し側が気づけるようにする。
    [Fact(DisplayName = "Viewport and speed validation rejects invalid geometry")]
    public void ViewportAndSpeedValidationRejectsInvalidGeometry()
    {
        var game = NewGame();
        Assert.Throws<ArgumentOutOfRangeException>(() => game.Configure(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.Configure(100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.Configure(1280, 3));
    }

    // 作業中に遊ぶアプリなので、通知をクリックしただけで走行が消えては使い物にならない。
    // 一時停止では得点も障害物も残り、記録もまだ確定しない。再開すると続きから走る。
    [Fact(DisplayName = "Pause keeps the run and resume continues it")]
    public void PauseKeepsTheRunAndResumeContinuesIt()
    {
        var finished = 0;
        var game = Playing();
        game.RunFinished += () => finished++;
        Advance(game, 1, 60);
        var score = game.Score;
        var obstacles = game.Obstacles.Count;
        var elapsed = game.Elapsed;

        game.Pause();
        Assert.True(game.State == GameState.Paused && finished == 0,
            $"Pause must not finish the run: state={game.State}, finished={finished}");
        Assert.True(game.Score == score && game.Obstacles.Count == obstacles,
            $"Pause must keep the score and the obstacles: {Snapshot(game)}");

        // 隠している間も画面を描く処理から Update が届きうる。止まっている間は進めてはいけない。
        Advance(game, 2, 60);
        Assert.True(game.Elapsed == elapsed && game.Score == score,
            $"A paused run must ignore Update: {Snapshot(game)}");
        // 押しっぱなし扱いのまま再開すると、勝手にしゃがんだり横に流れたりする。
        Assert.True(!game.Jump() && !game.IsDucking, "A paused run must ignore game input");

        game.Resume();
        Advance(game, .5, 60);
        Assert.True(game.State == GameState.Playing && game.Score > score && finished == 0,
            $"Resume must continue the same run: {Snapshot(game)}");
    }

    // 一時停止したまま終了する場面は普通に起きる（アプリを閉じる、設定を変えて閉じるなど）。
    // ここで記録を落とすと、遊んだ結果が黙って消える。確定は1回だけでなければ回数が水増しされる。
    [Fact(DisplayName = "Stopping a paused run records it exactly once")]
    public void StoppingAPausedRunRecordsItExactlyOnce()
    {
        var finished = 0;
        var game = Playing();
        game.RunFinished += () => finished++;
        Advance(game, 1, 60);
        var score = game.Score;

        game.Pause();
        game.Stop();
        Assert.True(finished == 1 && game.BestScore == score,
            $"Stopping a paused run must record it once: finished={finished}, best={game.BestScore}, score={score}");
        game.Stop();
        Assert.True(finished == 1, $"A finished run must not be recorded again: finished={finished}");
    }

    // Pause はプレイ中だけの操作。待機中やゲームオーバーから一時停止に入れてしまうと、
    // アイコンを押したときに「続きから」と判断され、始まっていない走行を再開しようとする。
    [Fact(DisplayName = "Pause and resume only apply to an active run")]
    public void PauseAndResumeOnlyApplyToAnActiveRun()
    {
        var game = NewGame();
        game.Pause();
        Assert.Equal(GameState.Idle, game.State);
        game.Ready();
        game.Pause();
        Assert.Equal(GameState.Ready, game.State);
        game.Resume();
        Assert.Equal(GameState.Ready, game.State);

        game.Start();
        Advance(game, 10, 60);
        Assert.Equal(GameState.GameOver, game.State);
        game.Pause();
        Assert.Equal(GameState.GameOver, game.State);
    }

    // 一時停止中でも、設定の変更や画面の解像度変更で Configure が呼ばれる。
    // ここで障害物や得点が消えると、再開した瞬間に何もない場所を走り出す。
    [Fact(DisplayName = "Configure during a pause keeps the run intact")]
    public void ConfigureDuringAPauseKeepsTheRunIntact()
    {
        var game = Playing();
        Advance(game, 2, 60);
        game.Pause();
        var score = game.Score;
        var obstacles = game.Obstacles.Count;

        game.Configure(900, 1.25);
        Assert.True(game.Score == score && game.Obstacles.Count == obstacles,
            $"Configure must not clear a paused run: {Snapshot(game)}");
        // 狭い画面に変わった場合でも、キャラクターは動ける範囲に収まっている必要がある。
        Assert.True(game.PlayerX >= game.MinimumPlayerX && game.PlayerX <= game.MaximumPlayerX,
            $"Configure must keep the player inside the new viewport: {Snapshot(game)}");
    }
}
