using System.Diagnostics;

namespace BetterClickPPT.Core.Deployment;

/// <summary>已安装实例的查找 / 关闭 / 启动。</summary>
public static class AppInstanceManager
{
    /// <summary>当前可执行文件路径。</summary>
    public static string CurrentExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var main = Process.GetCurrentProcess().MainModule?.FileName;
            return main ?? Path.Combine(AppContext.BaseDirectory, AppPaths.MainExecutableName);
        }
    }

    /// <summary>查找正在运行的 BetterClickPPT 进程（可限定安装目录）。</summary>
    public static IReadOnlyList<Process> FindRunningProcesses(string? installDirectory = null)
    {
        var result = new List<Process>();

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppPaths.MainExecutableName)))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    process.Dispose();
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(installDirectory))
                {
                    var path = process.MainModule?.FileName ?? string.Empty;
                    if (!path.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Dispose();
                        continue;
                    }
                }

                result.Add(process);
            }
            catch
            {
                process.Dispose();
            }
        }

        return result;
    }

    /// <summary>请求已运行实例优雅退出（通过命名事件）。</summary>
    public static bool RequestShutdown()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(AppPaths.ShutdownEventName, out var handle))
            {
                return false;
            }

            using (handle)
            {
                handle.Set();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>关闭所有正在运行的实例，返回关闭的进程数。</summary>
    public static int StopAll(string? installDirectory = null, int timeoutMs = 4000)
    {
        var processes = FindRunningProcesses(installDirectory);
        if (processes.Count == 0)
        {
            return 0;
        }

        RequestShutdown();

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && processes.Any(p => !HasExited(p)))
        {
            Thread.Sleep(150);
        }

        var stopped = 0;
        foreach (var process in processes)
        {
            try
            {
                if (!HasExited(process))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }

                stopped++;
            }
            catch
            {
                // 忽略关闭失败。
            }
            finally
            {
                process.Dispose();
            }
        }

        return stopped;
    }

    /// <summary>启动已安装的程序。</summary>
    public static Process? Start(string executablePath, string arguments = "--tray")
    {
        if (!File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            return Process.Start(new ProcessStartInfo(executablePath, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!
            });
        }
        catch
        {
            return null;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }
}
