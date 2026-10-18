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

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var v = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(v)) return false;

            // 程序被移动过则视为失效，允许覆盖
            var current = ExePath;
            if (string.IsNullOrEmpty(current)) return true;
            return v.Contains(current, StringComparison.OrdinalIgnoreCase)
                || v.Contains(System.IO.Path.GetFileName(current), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return false;

            if (enabled)
            {
                var exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" --tray", RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
