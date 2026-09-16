using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TaskbarRunner.Platform;

/// <summary>Windows にサインインしたときの自動起動を登録・解除する。</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // デバッグ版は別の名前で登録する
    private static string ValueName => Build.IsDebug ? "TaskbarRunner.Debug" : "TaskbarRunner";

    /// <summary>今、自動起動に登録されているか。読めない場合は登録なしとして扱う。</summary>
    internal static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>登録状態を変える。成功なら null、失敗なら利用者に見せる理由を返す。</summary>
    internal static string? SetEnabled(bool enabled)
    {
        var path = Environment.ProcessPath;
        if (enabled && string.IsNullOrEmpty(path))
            return "実行ファイルの場所が分からないため、自動起動を登録できませんでした。";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{path}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return $"自動起動の設定を変更できませんでした: {ex.Message}";
        }
    }
}
