using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BetterClickPPT.SelfTest;

/// <summary>启动 / 控制模拟放映进程（独立进程，尽量贴近真实场景）。</summary>
internal sealed class MockShowSession : IDisposable
{
    private static readonly Regex ClickPattern = new(@"CLICK_DOWN x=(-?\d+) y=(-?\d+)", RegexOptions.Compiled);

    private readonly Process? _process;
    private readonly string _logPath;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _output = new();

    private MockShowSession(Process? process, string logPath)
    {
        _process = process;
        _logPath = logPath;
    }

    /// <summary>模拟窗口句柄。</summary>
    public IntPtr Handle { get; private set; }

    /// <summary>模拟窗口矩形（屏幕坐标）。</summary>
    public System.Drawing.Rectangle Bounds { get; private set; }

    public string LogPath => _logPath;

    /// <summary>启动模拟放映程序。</summary>
    public static MockShowSession Start(bool fullScreen, int seconds = 20, string className = "screenClass")
    {
        var executable = FindMockExecutable();
        var logPath = Path.Combine(Path.GetTempPath(), "bcpp-mock-" + Guid.NewGuid().ToString("N")[..8] + ".log");

        var arguments = $"--log \"{logPath}\" --seconds {seconds} --class {className}"
                        + (fullScreen ? " --fullscreen" : string.Empty);

        var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        });

        var session = new MockShowSession(process, logPath);

        if (process is not null)
        {
            process.OutputDataReceived += (_, e) => session.Capture(e.Data);
            process.ErrorDataReceived += (_, e) => session.Capture(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        session.WaitForReady(8000);
        return session;
    }

    private void Capture(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            _output.Enqueue(line);
        }
    }

    private string DescribeProcessState()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append($"日志路径={_logPath} 存在={File.Exists(_logPath)}；");
        builder.Append("进程状态：");

        try
        {
            if (_process is null)
            {
                builder.Append("未启动；");
            }
            else if (_process.HasExited)
            {
                builder.Append($"已退出（退出码 {_process.ExitCode}）；");
            }
            else
            {
                builder.Append("运行中；");
            }
        }
        catch (Exception ex)
        {
            builder.Append($"未知（{ex.Message}）；");
        }

        if (!_output.IsEmpty)
        {
            builder.Append("输出：" + string.Join(" | ", _output.ToArray()));
        }

        return builder.ToString();
    }

    /// <summary>读取日志内容（允许与写入方并发）。</summary>
    public IReadOnlyList<string> ReadLog()
    {
        try
        {
            if (!File.Exists(_logPath))
            {
                return Array.Empty<string>();
            }

            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>是否存在包含指定片段的日志行。</summary>
    public bool HasEvent(string fragment)
        => ReadLog().Any(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>统计包含指定片段的日志行数。</summary>
    public int CountEvents(string fragment)
        => ReadLog().Count(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>窗口中心点（屏幕坐标）。</summary>
    public System.Drawing.Point Center => new(
        Bounds.Left + Bounds.Width / 2,
        Bounds.Top + Bounds.Height / 2);

    /// <summary>把客户端坐标换算成屏幕坐标。</summary>
    public System.Drawing.Point ClientToScreen(System.Drawing.Point clientPoint)
        => new(Bounds.Left + clientPoint.X, Bounds.Top + clientPoint.Y);

    /// <summary>取出所有“左键按下”的客户端坐标（按日志顺序）。</summary>
    public IReadOnlyList<System.Drawing.Point> GetClickedPoints()
    {
        var points = new List<System.Drawing.Point>();

        foreach (var line in ReadLog())
        {
            var match = ClickPattern.Match(line);
            if (match.Success)
            {
                points.Add(new System.Drawing.Point(
                    int.Parse(match.Groups[1].Value),
                    int.Parse(match.Groups[2].Value)));
            }
        }

        return points;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // 忽略。
        }
        finally
        {
            _process?.Dispose();
        }

        try
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
        }
        catch
        {
            // 忽略。
        }
    }

    private void WaitForReady(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            Pump.Wait(60);

            var readyLine = ReadLog().FirstOrDefault(line => line.Contains("READY", StringComparison.Ordinal));
            if (readyLine is null)
            {
                continue;
            }

            var handleMatch = Regex.Match(readyLine, @"hwnd=0x([0-9A-Fa-f]+)");
            var rectMatch = Regex.Match(readyLine, @"rect=(-?\d+),(-?\d+),(\d+)x(\d+)");

            if (!handleMatch.Success || !rectMatch.Success)
            {
                continue;
            }

            Handle = new IntPtr(Convert.ToInt64(handleMatch.Groups[1].Value, 16));
            Bounds = new System.Drawing.Rectangle(
                int.Parse(rectMatch.Groups[1].Value),
                int.Parse(rectMatch.Groups[2].Value),
                int.Parse(rectMatch.Groups[3].Value),
                int.Parse(rectMatch.Groups[4].Value));

            Pump.Wait(250);
            return;
        }

        throw new InvalidOperationException(
            $"模拟放映窗口启动超时。日志：{string.Join(" | ", ReadLog())}{Environment.NewLine}         {DescribeProcessState()}");
    }

    private static string FindMockExecutable()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "mock", "BetterClickPPT.MockShow.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "BetterClickPPT.MockShow.exe");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        TestRunner.Skip("未找到模拟放映程序（mock\\BetterClickPPT.MockShow.exe），请先执行 dotnet build。");
        throw new InvalidOperationException();
    }
}

/// <summary>消息泵辅助（钩子与原始输入都需要线程持续抽消息）。</summary>
internal static class Pump
{
    /// <summary>等待指定毫秒，期间持续抽消息，保证钩子回调能执行。</summary>
    public static void Wait(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
