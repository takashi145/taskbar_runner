namespace TaskbarRunner;

/// <summary>
/// ビルド構成による違いを1か所にまとめる。デバッグ版と配布版は同時に起動できるので、
/// どちらを触っているのかが見た目で分かるようにする。
/// </summary>
internal static class Build
{
#if DEBUG
    internal static readonly bool IsDebug = true;
#else
    internal static readonly bool IsDebug = false;
#endif
}
