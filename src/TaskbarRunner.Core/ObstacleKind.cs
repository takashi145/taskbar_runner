namespace TaskbarRunner.Core;

/// <summary>障害物の種類。大きさは Obstacle、いつ出すかは GameSession で決める。</summary>
public enum ObstacleKind
{
    // 地面にある低い障害物。1回のジャンプで飛び越える。
    GroundBar,
    // 空中に浮いた障害物。しゃがんで下を通る。
    OverheadBar,
    // 高い障害物。2段ジャンプで飛び越える。
    TallBar,
    // 幅広の障害物。2回目のジャンプを遅らせ、空中にいる時間を延ばして飛び越える。
    WideBar
}
