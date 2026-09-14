namespace TaskbarRunner.Core;

/// <summary>障害物1個の位置、大きさ、衝突を調べる範囲を表す。種類と幅は作るときに決め、プレイ中は横に動かす。</summary>
public sealed class Obstacle(double x, ObstacleKind kind = ObstacleKind.GroundBar, double? width = null)
{
    public double X { get; internal set; } = x;
    public ObstacleKind Kind { get; } = kind;
    public double Width { get; } = width ?? kind switch
    {
        ObstacleKind.OverheadBar => 80,
        ObstacleKind.WideBar => 120,
        _ => 20
    };
    // 空中の障害物は2段ジャンプでも飛び越せない高さにする。衝突を調べる範囲が見た目より小さいことも見込む。
    public double Height => Kind switch
    {
        ObstacleKind.OverheadBar => 80,
        ObstacleKind.TallBar => 90,
        _ => 38
    };
    // 障害物を地面からどれだけ浮かせるか。空中の障害物は、しゃがんで下を通れる高さにする。
    public double Clearance => Kind == ObstacleKind.OverheadBar ? 34 : 0;
    // 地面を Y=0 とし、上にあるほどマイナスの値にする。衝突を調べる範囲は、見た目より少し小さくする。
    public Hitbox Hitbox => new(X + 4, -Clearance - Height + 3, Width - 8,
        Height - (Kind == ObstacleKind.OverheadBar ? 6 : 3));
}
