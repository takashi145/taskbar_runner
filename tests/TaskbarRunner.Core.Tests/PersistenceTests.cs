using TaskbarRunner.Core;
using Xunit;
using static TaskbarRunner.Tests.GameTestSupport;

namespace TaskbarRunner.Tests;

/// <summary>
/// 設定と記録の保存を確かめる。保存先はユーザーが直接開ける AppData のファイルなので、
/// 手で書き換えられていたり、書き込みの途中で電源が落ちていたりする前提で扱う必要がある。
/// 「読めなかったらアプリが起動しない」が一番避けたい失敗。
/// </summary>
public sealed class PersistenceTests
{
    // ファイルを手で編集されても壊れないよう、範囲外の値は読み込み時に直す。
    // FPS のような選択肢が決まっている項目は既定値へ、数値は上限下限へ丸める。
    [Fact(DisplayName = "Settings and saves normalize out-of-range values")]
    public void SettingsAndSavesNormalizeOutOfRangeValues()
    {
        var settings = new GameSettings { FpsLimit = 1, GameSpeed = double.NaN, CharacterScale = 10, DisplayOffset = -20 }.Sanitize();
        Assert.True(settings.FpsLimit == 60 && settings.GameSpeed == 1 && settings.CharacterScale == 1.5 && settings.DisplayOffset == 0);
        // 旧版の120 FPS設定は60へ移行し、現行の選択肢は維持する。
        Assert.True(new GameSettings { FpsLimit = 120 }.Sanitize().FpsLimit == 60);
        foreach (var fps in new[] { 30, 60 })
            Assert.True(new GameSettings { FpsLimit = fps }.Sanitize().FpsLimit == fps);
        var save = new SaveData { BestScore = -20, TotalRuns = -1, TotalDistance = double.PositiveInfinity }.Sanitize();
        Assert.True(save.BestScore == 0 && save.TotalRuns == 0 && save.TotalDistance == 0);
    }

    // 保存して読み直すと同じ値に戻ること。加えて、書き込みは一時ファイル経由で差し替えるので、
    // 成功後に .tmp が残っていないことを確かめる。最後の1行は保存形式（camelCase）の固定で、
    // キー名が変わると利用者の既存ファイルが読めなくなる。
    [Fact(DisplayName = "Local persistence round-trips and replaces files atomically")]
    public void LocalPersistenceRoundTripsAndReplacesFilesAtomically() => WithTemp(folder =>
    {
        var path = Path.Combine(folder, "nested", "save.json");
        var store = new JsonStore<SaveData>(path);
        Assert.True(store.Load().BestScore == 0 && store.LastError is null);
        Assert.True(store.Save(new SaveData { BestScore = 123, TotalRuns = 4 }));
        Assert.True(store.Save(new SaveData { BestScore = 456, TotalRuns = 5 }));
        var loaded = store.Load();
        Assert.True(loaded.BestScore == 456 && loaded.TotalRuns == 5);
        Assert.True(!File.Exists(path + ".tmp"));
        Assert.Contains("\"bestScore\"", File.ReadAllText(path));
    });
    // 壊れた JSON でも例外で落ちず、既定値で起動する。ただし黙って握りつぶすと利用者は
    // 記録が消えた理由が分からないので、LastError に理由を残す。元のファイルは上書きせず
    // そのまま残し、後から中身を救い出せるようにする。
    [Fact(DisplayName = "Corrupt save falls back with an actionable error")]
    public void CorruptSaveFallsBackWithAnActionableError() => WithTemp(folder =>
    {
        var path = Path.Combine(folder, "save.json");
        File.WriteAllText(path, "{ corrupt");
        var store = new JsonStore<SaveData>(path);
        Assert.True(store.Load().BestScore == 0 && store.LastError is not null);
        Assert.True(File.ReadAllText(path) == "{ corrupt");
    });
    // 保存に失敗する状況を、ファイルをフォルダー代わりに使わせて再現する。
    // 失敗しても false を返すだけで、巻き添えで既存のファイルを壊さないことを確かめる。
    [Fact(DisplayName = "Save failure is reported without destroying existing content")]
    public void SaveFailureIsReportedWithoutDestroyingExistingContent() => WithTemp(folder =>
    {
        var blocker = Path.Combine(folder, "not-a-directory");
        File.WriteAllText(blocker, "keep");
        var store = new JsonStore<SaveData>(Path.Combine(blocker, "save.json"));
        Assert.True(!store.Save(new SaveData { BestScore = 100 }) && store.LastError is not null);
        Assert.True(File.ReadAllText(blocker) == "keep");
    });

}
