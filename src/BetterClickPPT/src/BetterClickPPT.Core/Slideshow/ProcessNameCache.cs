using System.Collections.Concurrent;
using System.Diagnostics;

namespace BetterClickPPT.Core.Slideshow;

/// <summary>
/// 进程名缓存。鼠标钩子回调必须极快，因此这里对 PID → 进程名做 5 秒缓存，
/// 避免在钩子回调里反复查询进程信息导致钩子超时。
/// </summary>
public static class ProcessNameCache
{
    private const int TtlMs = 5000;
    private const int MaxEntries = 1024;

    private static readonly ConcurrentDictionary<uint, Entry> Cache = new();

    private readonly record struct Entry(string Name, long Tick);

    /// <summary>获取进程名（含 .exe，例如 POWERPNT.EXE）；失败时返回空字符串。</summary>
    public static string GetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        var now = Environment.TickCount64;
        if (Cache.TryGetValue(pid, out var entry) && now - entry.Tick < TtlMs)
        {
            return entry.Name;
        }

        var name = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName + ".exe";
        }
        catch
        {
            name = string.Empty;
        }

        if (Cache.Count > MaxEntries)
        {
            Cache.Clear();
        }

        Cache[pid] = new Entry(name, now);
        return name;
    }

    /// <summary>清除指定进程的缓存（进程退出 / 更换映像时使用）。</summary>
    public static void Invalidate(uint pid) => Cache.TryRemove(pid, out _);

    /// <summary>清空缓存。</summary>
    public static void Clear() => Cache.Clear();
}
