using System.Text;

namespace BetterClickPPT.Core.Logging;

/// <summary>日志级别。</summary>
public enum LogLevel
{
    /// <summary>调试信息（仅在“详细日志”开启时写入）。</summary>
    Trace,

    /// <summary>普通信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warn,

    /// <summary>错误。</summary>
    Error
}

/// <summary>一条日志记录。</summary>
public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Timestamp:HH:mm:ss.fff} [{Level}] {Message}";
}

/// <summary>
/// 轻量级线程安全文件日志。写入 %LOCALAPPDATA%\BetterClickPPT\logs\betterclickppt.log，
/// 超过大小上限时自动滚动（保留一份 .old）。
/// </summary>
public sealed class AppLog
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly object _sync = new();
    private readonly int _maxBytes;
    private bool _verbose;

    public AppLog(string? filePath = null, bool verbose = false, int maxKb = 512)
    {
        FilePath = filePath ?? AppPaths.LogFile;
        _verbose = verbose;
        _maxBytes = Math.Max(64, maxKb) * 1024;
    }

    /// <summary>日志文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>是否记录 Trace 级别日志。</summary>
    public bool Verbose
    {
        get => _verbose;
        set => _verbose = value;
    }

    /// <summary>写入一条日志时触发（供界面实时显示）。</summary>
    public event EventHandler<LogEntry>? EntryWritten;

    public void Trace(string message) => Write(LogLevel.Trace, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    /// <summary>记录异常。</summary>
    public void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    public void Write(LogLevel level, string message)
    {
        if (level == LogLevel.Trace && !_verbose)
        {
            return;
        }

        var entry = new LogEntry(DateTime.Now, level, message);

        try
        {
            lock (_sync)
            {
                AppPaths.EnsureDirectory(Path.GetDirectoryName(FilePath)!);
                RotateIfNeeded();

                // 使用带 BOM 的 UTF-8：记事本 / PowerShell 直接打开日志不会乱码。
                File.AppendAllText(FilePath, entry + Environment.NewLine, Utf8WithBom);
            }
        }
        catch
        {
            // 日志失败绝不能影响主流程。
        }

        try
        {
            EntryWritten?.Invoke(this, entry);
        }
        catch
        {
            // 忽略订阅者异常。
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length < _maxBytes)
            {
                return;
            }

            var old = FilePath + ".old";
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(FilePath, old);
        }
        catch
        {
            // 忽略滚动失败。
        }
    }
}
