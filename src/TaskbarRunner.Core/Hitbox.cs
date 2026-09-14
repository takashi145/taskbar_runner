namespace TaskbarRunner.Core;

/// <summary>ぶつかったかどうかを調べる四角い範囲。左上の位置、幅、高さで表し、傾きはない。</summary>
public readonly record struct Hitbox(double X, double Y, double Width, double Height)
{
    /// <summary>2つの四角形が重なったら、ぶつかったとする。端が触れるだけなら、ぶつかったことにしない。</summary>
    public bool Intersects(Hitbox other) => X < other.X + other.Width &&
        X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y;
}
