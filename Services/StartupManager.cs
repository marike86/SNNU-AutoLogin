using System;
using Microsoft.Win32;

namespace SNNUAutoLogin.Services;

/// <summary>开机自启动（HKCU Run，无需管理员权限）。</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SNNUAutoLogin";

    private static string ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            return string.IsNullOrEmpty(p) ? "" : p;
        }
    }

    public static bool IsEnabled() => IsEnabled(ExePath, RunKey);

    internal static bool IsEnabled(string executablePath, string runKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey, false);
            var v = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(v)) return false;

            // 程序被移动过则视为失效，允许覆盖
            return !string.IsNullOrEmpty(executablePath)
                && string.Equals(v, Command(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Command(string executablePath) => $"\"{executablePath}\" --tray";

    public static bool SetEnabled(bool enabled) => SetEnabled(enabled, ExePath, RunKey);

    internal static bool SetEnabled(bool enabled, string executablePath, string runKey)
    {
        try
        {
            if (enabled)
            {
                if (string.IsNullOrEmpty(executablePath)) return false;
                using var key = Registry.CurrentUser.CreateSubKey(runKey, true);
                if (key == null) return false;
                key.SetValue(ValueName, Command(executablePath), RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
                key?.DeleteValue(ValueName, false);
            }
            return IsEnabled(executablePath, runKey) == enabled;
        }
        catch
        {
            return false;
        }
    }
}
