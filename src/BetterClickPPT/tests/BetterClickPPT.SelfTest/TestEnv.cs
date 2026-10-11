namespace BetterClickPPT.SelfTest;

/// <summary>测试辅助方法。</summary>
internal static class TestEnv
{
    private static readonly string SharedLogDirectory = CreateTempDirectory();

    static TestEnv()
    {
        // 进程退出时清理自测产生的临时目录，避免在 %TEMP% 留下垃圾。
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteDirectory(SharedLogDirectory);
    }

    /// <summary>创建临时目录。</summary>
    public static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bcpp-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>删除目录（忽略失败）。</summary>
    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 忽略。
        }
    }

    /// <summary>创建测试日志（所有测试共用一个临时目录，进程退出时自动清理）。</summary>
    public static Core.Logging.AppLog CreateLog()
        => new(Path.Combine(SharedLogDirectory, "selftest-" + Guid.NewGuid().ToString("N")[..6] + ".log"), verbose: true, maxKb: 4096);
}
