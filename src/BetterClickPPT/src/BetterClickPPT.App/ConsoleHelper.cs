using System.Runtime.InteropServices;
using System.Text;

namespace BetterClickPPT.App;

/// <summary>
/// 控制台辅助：本程序是 GUI 程序（WinExe），从命令行运行时需要附加到父控制台才能输出文字，
/// 这样 --detect / --install 等命令行模式才便于自动化与排查。
/// </summary>
internal static class ConsoleHelper
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    private static bool _attached;

    /// <summary>尝试附加到父进程的控制台。</summary>
    public static void AttachToParent()
    {
        if (_attached)
        {
            return;
        }

        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                // 统一使用 UTF-8：避免中文与设备路径等在父控制台中变成乱码。
                try
                {
                    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                }
                catch
                {
                    // 个别宿主控制台不允许修改编码时忽略。
                }

                var stdout = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdout, Console.OutputEncoding) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
        }
        catch
        {
            // 没有父控制台（例如从资源管理器双击启动）时忽略。
        }

        _attached = true;
    }

    /// <summary>输出一行（同时写入 --out 指定的文件）。</summary>
    public static void WriteLine(string message, TextWriter? fileWriter = null)
    {
        Console.WriteLine(message);
        fileWriter?.WriteLine(message);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
}
