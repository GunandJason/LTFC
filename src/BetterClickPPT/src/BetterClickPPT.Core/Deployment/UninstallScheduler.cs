using System.Diagnostics;
using BetterClickPPT.Core.Logging;

namespace BetterClickPPT.Core.Deployment;

/// <summary>
/// 卸载时删除自身：先把清理程序复制到临时目录，再等主进程退出后删除安装目录，
/// 最后删除临时目录（Windows 下正在运行的程序无法删除自己，只能用这个办法）。
/// </summary>
public static class UninstallScheduler
{
    /// <summary>清理模式命令行参数。</summary>
    public const string CleanupArgument = "--cleanup";

    /// <summary>启动一个临时的清理进程。</summary>
    public static bool ScheduleCleanup(string installDirectory, bool deleteUserData, AppLog? log = null)
    {
        try
        {
            var source = AppInstanceManager.CurrentExecutablePath;
            var sourceDirectory = Path.GetDirectoryName(source);
            var tempDirectory = Path.Combine(Path.GetTempPath(), "BetterClickPPT-cleanup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDirectory);

            // 把“当前程序所在的整个目录”复制到临时目录再运行清理逻辑：
            // 单文件版只有一个 exe，多文件版（开发期输出目录）也能正常复制。
            var copied = 0;
            if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceDirectory, file);
                    var destination = Path.Combine(tempDirectory, relative);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(file, destination, overwrite: true);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        // 个别文件被占用时跳过（不影响清理逻辑本身）。
                        log?.Trace($"复制清理所需文件失败（跳过）：{relative} / {ex.Message}");
                    }
                }
            }

            var target = Path.Combine(tempDirectory, AppPaths.MainExecutableName);
            if (!File.Exists(target))
            {
                File.Copy(source, target, overwrite: true);
                copied++;
            }

            var arguments = $"{CleanupArgument} --dir \"{installDirectory}\" --wait-pid {Environment.ProcessId} --delete-self";
            if (deleteUserData)
            {
                arguments += " --delete-userdata";
            }

            var process = Process.Start(new ProcessStartInfo(target, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = tempDirectory
            });

            process?.Dispose();
            log?.Info($"已安排清理进程（复制 {copied} 个文件），将删除目录：{installDirectory}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Error("安排卸载清理失败", ex);
            return false;
        }
    }

    /// <summary>执行清理（由临时副本进程调用）。</summary>
    public static int RunCleanup(string installDirectory, int waitProcessId, bool deleteSelf, bool deleteUserData)
    {
        WaitForProcessExit(waitProcessId, 20000);

        if (deleteUserData)
        {
            DeleteDirectoryWithRetry(AppPaths.DataRoot, attempts: 10);
        }

        // 被占用时（例如托盘程序尚未完全退出）多次重试，最多约 15 秒。
        DeleteDirectoryWithRetry(installDirectory, attempts: 30);

        if (deleteSelf)
        {
            ScheduleSelfDelete();
        }

        return 0;
    }

    private static void WaitForProcessExit(int processId, int timeoutMs)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(timeoutMs);
        }
        catch
        {
            // 进程已退出。
        }
    }

    private static bool DeleteDirectoryWithRetry(string directory, int attempts)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return true;
        }

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch
            {
                Thread.Sleep(400);
            }
        }

        return !Directory.Exists(directory);
    }

    private static void ScheduleSelfDelete()
    {
        try
        {
            var directory = Path.GetDirectoryName(AppInstanceManager.CurrentExecutablePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var command = $"/c ping -n 2 127.0.0.1 >nul & rmdir /s /q \"{directory}\"";
            var process = Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            process?.Dispose();
        }
        catch
        {
            // 忽略自删除失败（残留临时目录不影响使用）。
        }
    }
}
