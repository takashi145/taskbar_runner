using System.Text.Json;

namespace TaskbarRunner.Core;

/// <summary>設定や記録をファイルに保存し、読み込む。読み書きの失敗理由は LastError に残し、アプリ側で表示する。</summary>
public sealed class JsonStore<T>(string path) where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    /// <summary>最後に読み書きしたときの失敗理由。次の読み書きを始めるときに消す。</summary>
    public string? LastError { get; private set; }

    /// <summary>ファイルを読み込む。まだファイルがない場合や読めない場合は、初期値のデータを作って返す。</summary>
    public T Load()
    {
        LastError = null;
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"{Path.GetFileName(path)} を読み込めませんでした。既定値を使用します。{ex.Message}";
            return new T();
        }
    }

    /// <summary>ファイルに保存できたら true、失敗したら false を返す。</summary>
    public bool Save(T value)
    {
        LastError = null;
        var temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            // 書いている途中で失敗しても、前回保存した内容が壊れないようにする。
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"{Path.GetFileName(path)} を保存できませんでした。{ex.Message}";
            return false;
        }
        finally
        {
            // 入れ替えに失敗して仮のファイルが残ったら消す。消せなくても、保存の成功・失敗やその理由は変えない。
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
