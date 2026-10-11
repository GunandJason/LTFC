using Microsoft.Win32;

namespace BetterClickPPT.Core.Deployment;

/// <summary>注册表读取工具（同时兼容 32 / 64 位视图）。</summary>
public static class RegistryUtil
{
    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };

    /// <summary>读取字符串值；不存在时返回 null。</summary>
    public static string? ReadString(RegistryHive hive, RegistryView view, string subKeyPath, string? valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            var value = key?.GetValue(valueName ?? string.Empty) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>在两个注册表视图中依次读取。</summary>
    public static string? ReadStringAnyView(RegistryHive hive, string subKeyPath, string? valueName)
    {
        foreach (var view in Views)
        {
            var value = ReadString(hive, view, subKeyPath, valueName);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>读取 “App Paths\xxx.exe” 中登记的程序路径。</summary>
    public static string? GetAppPath(string executableName)
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}";
        return ReadStringAnyView(RegistryHive.LocalMachine, subKey, null)
            ?? ReadStringAnyView(RegistryHive.CurrentUser, subKey, null);
    }

    /// <summary>由 ProgID 找到 COM 服务器程序路径（例如 PowerPoint.Application）。</summary>
    public static string? GetProgIdServerPath(string progId)
    {
        foreach (var view in Views)
        {
            var clsid = ReadString(RegistryHive.ClassesRoot, view, progId + @"\CLSID", null)
                ?? ReadString(RegistryHive.CurrentUser, view, @"SOFTWARE\Classes\" + progId + @"\CLSID", null);
            if (string.IsNullOrWhiteSpace(clsid))
            {
                continue;
            }

            foreach (var serverKind in new[] { "LocalServer32", "InprocServer32" })
            {
                foreach (var hive in new[] { RegistryHive.ClassesRoot, RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    var path = ReadString(hive, view, $@"CLSID\{clsid}\{serverKind}", null);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var normalized = NormalizeExecutablePath(path);
                    if (normalized is not null)
                    {
                        return normalized;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>从可能带参数 / 引号 / 环境变量的命令串中提取可执行文件路径。</summary>
    public static string? NormalizeExecutablePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1)
            {
                return text[1..end];
            }
        }

        // 形如 "C:\Path\App.exe" /automation 或 C:\Path\App.exe -x
        var exeIndex = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return text[..(exeIndex + 4)].Trim().Trim('"');
        }

        return text.Split(' ')[0].Trim('"');
    }

    /// <summary>枚举卸载项（用于兜底搜索已安装的 Office / WPS）。</summary>
    public static IEnumerable<(string DisplayName, string? InstallLocation, string? DisplayIcon)> EnumerateUninstallEntries()
    {
        var roots = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32)
        };

        foreach (var (hive, view) in roots)
        {
            var results = new List<(string, string?, string?)>();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(name);
                        var displayName = entry?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        results.Add((displayName, entry?.GetValue("InstallLocation") as string, entry?.GetValue("DisplayIcon") as string));
                    }
                    catch
                    {
                        // 忽略单个卸载项的错误。
                    }
                }
            }
            catch
            {
                // 忽略整个视图的错误。
            }

            foreach (var item in results)
            {
                yield return item;
            }
        }
    }
}
