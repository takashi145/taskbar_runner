namespace TaskbarRunner.Core;

/// <summary>ゲームが今どの状態かを表す。中断すると Idle に戻る。</summary>
public enum GameState
{
    // ゲーム画面を隠している。時計の近くにあるロボットのアイコンから操作できる。
    Idle,
    // ゲーム画面を表示している。SPACE キーを押すと始まる。
    Ready,
    // プレイ中。キー操作でキャラクターが動き、障害物が流れ、得点が増える。
    Playing,
    // 障害物にぶつかって止まっている。SPACE キーでもう一度遊べる。
    GameOver
}
