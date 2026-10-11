using System.Text;

namespace BetterClickPPT.MockShow;

/// <summary>日志输出（同时写控制台与文件）。</summary>
internal sealed class LogWriter : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly object _sync = new();

    public LogWriter(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 使用 FileShare.ReadWrite：测试进程需要同时读取日志文件做断言。
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public void WriteLine(string message)
    {
        lock (_sync)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            _file?.WriteLine(line);

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // 无控制台时忽略。
            }
        }
    }

    public void Dispose() => _file?.Dispose();
}
