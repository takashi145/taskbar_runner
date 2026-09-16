using System;

namespace TaskbarRunner;

// トレイとアプリの操作を結ぶ。どの操作もUIスレッドへ渡してから実行する。
internal sealed record AppCommands(Action Play, Action Settings, Action RestartOverlay, Action Exit);
