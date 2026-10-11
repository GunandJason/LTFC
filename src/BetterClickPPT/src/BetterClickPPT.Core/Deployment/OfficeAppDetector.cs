using System.Diagnostics;
using Microsoft.Win32;

namespace BetterClickPPT.Core.Deployment;

/// <summary>检测到的目标软件。</summary>
public sealed record DetectedApp(string Id, string DisplayName, string? ExecutablePath, string? Version, string Source)
{
    /// <summary>是否真的找到了可执行文件。</summary>
    public bool IsFound => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    /// <summary>界面显示用的状态文字。</summary>
    public string StatusText => IsFound
        ? $"已检测到：{ExecutablePath}"
        : "未检测到，请手动选择主程序位置";

    public static DetectedApp NotFound(string id, string displayName, string reason = "") =>
        new(id, displayName, null, null, reason);
}

/// <summary>
/// 自动查找 Microsoft PowerPoint 与 WPS 演示的安装位置。
/// 依次尝试：App Paths → COM 注册信息 → Office / WPS 安装信息 → 常见目录 →
/// 卸载列表 → 正在运行的进程。
/// </summary>
public static class OfficeAppDetector
{
    private static readonly string[] PowerPointExeNames = { "POWERPNT.EXE", "powerpnt.exe" };
    private static readonly string[] WpsExeNames = { "wpp.exe", "wpp64.exe", "kwpp.exe", "wpspresent.exe" };

    /// <summary>检测全部目标软件。</summary>
    public static IReadOnlyList<DetectedApp> DetectAll() => new[] { DetectPowerPoint(), DetectWpsPresentation() };

    /// <summary>检测 Microsoft PowerPoint。</summary>
    public static DetectedApp DetectPowerPoint()
    {
        const string displayName = "Microsoft PowerPoint";

        foreach (var exeName in PowerPointExeNames)
        {
            var appPath = RegistryUtil.GetAppPath(exeName);
            if (IsFile(appPath))
            {
                return Create("powerpoint", displayName, appPath!, "App Paths 注册信息");
            }
        }

        var progIdServer = RegistryUtil.GetProgIdServerPath("PowerPoint.Application");
        if (IsFile(progIdServer))
        {
            return Create("powerpoint", displayName, progIdServer!, "COM 注册信息");
        }

        var clickToRun = TryClickToRunInstallation();
        if (IsFile(clickToRun))
        {
            return Create("powerpoint", displayName, clickToRun!, "Office 安装信息");
        }

        foreach (var version in new[] { "16.0", "15.0", "14.0", "12.0" })
        {
            var root = RegistryUtil.ReadStringAnyView(
                RegistryHive.LocalMachine,
                $@"SOFTWARE\Microsoft\Office\{version}\PowerPoint\InstallRoot",
                "Path");
            var candidate = CombineIfPossible(root, "POWERPNT.EXE");
            if (IsFile(candidate))
            {
                return Create("powerpoint", displayName, candidate!, "Office 安装信息");
            }
        }

        foreach (var candidate in EnumeratePowerPointDirectories())
        {
            if (IsFile(candidate))
            {
                return Create("powerpoint", displayName, candidate, "常见安装目录");
            }
        }

        var fromUninstall = SearchUninstallEntries("PowerPoint", PowerPointExeNames, "POWERPNT.EXE");
        if (IsFile(fromUninstall))
        {
            return Create("powerpoint", displayName, fromUninstall!, "卸载列表");
        }

        var fromProcess = FindRunningProcessPath(PowerPointExeNames);
        if (IsFile(fromProcess))
        {
            return Create("powerpoint", displayName, fromProcess!, "正在运行的进程");
        }

        return DetectedApp.NotFound("powerpoint", displayName);
    }

    /// <summary>检测 WPS 演示。</summary>
    public static DetectedApp DetectWpsPresentation()
    {
        const string displayName = "WPS 演示";

        foreach (var exeName in WpsExeNames)
        {
            var appPath = RegistryUtil.GetAppPath(exeName);
            if (IsFile(appPath))
            {
                return Create("wps", displayName, appPath!, "App Paths 注册信息");
            }
        }

        foreach (var progId in new[] { "KWPP.Application", "WPP.Application", "KWPS.Application" })
        {
            var server = RegistryUtil.GetProgIdServerPath(progId);
            if (IsFile(server))
            {
                return Create("wps", displayName, server!, "COM 注册信息");
            }
        }

        foreach (var installRoot in EnumerateWpsInstallRoots())
        {
            var candidate = FindWpsExecutable(installRoot);
            if (IsFile(candidate))
            {
                return Create("wps", displayName, candidate!, "WPS 安装信息");
            }
        }

        foreach (var candidate in EnumerateWpsDirectories())
        {
            if (IsFile(candidate))
            {
                return Create("wps", displayName, candidate, "常见安装目录");
            }
        }

        var fromUninstall = SearchUninstallEntries("WPS", WpsExeNames, "wpp.exe");
        if (IsFile(fromUninstall))
        {
            return Create("wps", displayName, fromUninstall!, "卸载列表");
        }

        var fromProcess = FindRunningProcessPath(WpsExeNames);
        if (IsFile(fromProcess))
        {
            return Create("wps", displayName, fromProcess!, "正在运行的进程");
        }

        return DetectedApp.NotFound("wps", displayName);
    }

    private static DetectedApp Create(string id, string displayName, string path, string source)
        => new(id, displayName, path, TryGetFileVersion(path), source);

    private static bool IsFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? TryGetFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string? CombineIfPossible(string? directory, string fileName)
        => string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, fileName);

    private static string? TryClickToRunInstallation()
    {
        var installationPath = RegistryUtil.ReadStringAnyView(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
            "InstallationPath");

        if (string.IsNullOrWhiteSpace(installationPath))
        {
            return null;
        }

        foreach (var relative in new[] { @"root\Office16\POWERPNT.EXE", @"root\Office15\POWERPNT.EXE", @"Office16\POWERPNT.EXE" })
        {
            var candidate = Path.Combine(installationPath, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePowerPointDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            foreach (var relative in new[]
                     {
                         @"Microsoft Office\root\Office16\POWERPNT.EXE",
                         @"Microsoft Office\root\Office15\POWERPNT.EXE",
                         @"Microsoft Office\Office16\POWERPNT.EXE",
                         @"Microsoft Office\Office15\POWERPNT.EXE",
                         @"Microsoft Office\Office14\POWERPNT.EXE"
                     })
            {
                yield return Path.Combine(root, relative);
            }

            foreach (var found in SearchFiles(root, "Microsoft Office", "POWERPNT.EXE", 3))
            {
                yield return found;
            }
        }
    }

    /// <summary>WPS 常见安装根目录（注册表 + 常见路径）。</summary>
    private static IEnumerable<string> EnumerateWpsInstallRoots()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Kingsoft\Office\WPS\InstallRoot",
            @"SOFTWARE\Kingsoft\Office\WPS\Common",
            @"SOFTWARE\Kingsoft\Office\6.0\common",
            @"SOFTWARE\Kingsoft\Office\6.0\common\InstallRoot",
            @"SOFTWARE\Kingsoft\Office\WPS"
        };

        var valueNames = new string?[] { null, "InstallRoot", "Path", "InstallPath" };

        foreach (var keyPath in keyPaths)
        {
            foreach (var valueName in valueNames)
            {
                var value = RegistryUtil.ReadStringAnyView(RegistryHive.LocalMachine, keyPath, valueName)
                    ?? RegistryUtil.ReadStringAnyView(RegistryHive.CurrentUser, keyPath, valueName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateWpsDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kingsoft")
        };

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var baseDirectory = Path.Combine(root, "Kingsoft");
            foreach (var found in SearchFiles(baseDirectory, null, "wpp.exe", 3))
            {
                yield return found;
            }
        }
    }

    private static string? FindWpsExecutable(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        foreach (var exeName in WpsExeNames)
        {
            foreach (var relative in new[] { exeName, Path.Combine("office6", exeName) })
            {
                var candidate = Path.Combine(installRoot, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return Directory.Exists(installRoot)
            ? SearchFiles(installRoot, null, "wpp.exe", 2).FirstOrDefault()
            : null;
    }

    /// <summary>在指定目录下浅层搜索文件（限制深度与耗时，避免拖慢安装向导）。</summary>
    private static IEnumerable<string> SearchFiles(string? root, string? subDirectory, string fileName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        var start = string.IsNullOrWhiteSpace(subDirectory) ? root : Path.Combine(root, subDirectory);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var stopwatch = Stopwatch.StartNew();
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0 && stopwatch.ElapsedMilliseconds < 1500)
        {
            var (directory, depth) = queue.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            if (files.Length > 0)
            {
                yield return files[0];
                yield break;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            try
            {
                foreach (var child in Directory.GetDirectories(directory))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch
            {
                // 忽略无权限目录。
            }
        }
    }

    private static string? SearchUninstallEntries(string keyword, string[] exeNames, string preferredExeName)
    {
        foreach (var (displayName, installLocation, displayIcon) in RegistryUtil.EnumerateUninstallEntries())
        {
            if (displayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                foreach (var exeName in exeNames.Prepend(preferredExeName))
                {
                    foreach (var relative in new[]
                             {
                                 exeName,
                                 Path.Combine("office6", exeName),
                                 Path.Combine("Office16", exeName)
                             })
                    {
                        var candidate = Path.Combine(installLocation, relative);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }

                var found = SearchFiles(installLocation, null, preferredExeName, 2).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }

            var iconPath = RegistryUtil.NormalizeExecutablePath(displayIcon);
            if (IsFile(iconPath) && string.Equals(Path.GetFileName(iconPath), preferredExeName, StringComparison.OrdinalIgnoreCase))
            {
                return iconPath;
            }
        }

        return null;
    }

    private static string? FindRunningProcessPath(string[] exeNames)
    {
        var wanted = exeNames.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!wanted.Contains(process.ProcessName))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (IsFile(path))
                {
                    return path;
                }
            }
            catch
            {
                // 忽略无权限 / 已退出的进程。
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }
}
