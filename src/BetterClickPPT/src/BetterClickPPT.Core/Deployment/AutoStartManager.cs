using Microsoft.Win32;

namespace BetterClickPPT.Core.Deployment;

/// <summary>开机自启动管理（HKCU，无需管理员权限）。</summary>
public static class AutoStartManager
{
    /// <summary>是否已启用开机自启。</summary>
    public static bool IsEnabled(string? installDirectory = null)
    {
        var command = GetCommandLine();
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return true;
        }

        return command.IndexOf(installDirectory, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>读取当前自启命令行。</summary>
    public static string? GetCommandLine()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppPaths.RunRegistryKey, writable: false);
            return key?.GetValue(AppPaths.RunValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>启用开机自启（静默启动到托盘）。</summary>
    public static void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPaths.RunRegistryKey, writable: true);
        key?.SetValue(AppPaths.RunValueName, $"\"{executablePath}\" --tray", RegistryValueKind.String);
    }

    /// <summary>关闭开机自启。</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppPaths.RunRegistryKey, writable: true);
            key?.DeleteValue(AppPaths.RunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 忽略删除失败。
        }
    }
}
