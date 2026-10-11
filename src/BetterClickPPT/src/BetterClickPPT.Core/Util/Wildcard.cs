using System.Text;
using System.Text.RegularExpressions;

namespace BetterClickPPT.Core.Util;

/// <summary>
/// 极简的通配符匹配（支持 * 和 ?），不区分大小写，用于匹配进程名 / 窗口类名。
/// </summary>
public static class Wildcard
{
    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);
    private static readonly object Sync = new();

    /// <summary>判断 <paramref name="input"/> 是否匹配 <paramref name="pattern"/>（例如 "POWERPNT.EXE"、"wpp*.exe"）。</summary>
    public static bool IsMatch(string? input, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        var regex = GetRegex(pattern);
        return regex.IsMatch(input);
    }

    /// <summary>集合中任意一项匹配即返回 true。</summary>
    public static bool IsMatchAny(string? input, IEnumerable<string>? patterns)
    {
        if (string.IsNullOrEmpty(input) || patterns is null)
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (IsMatch(input, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex GetRegex(string pattern)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(pattern, out var cached))
            {
                return cached;
            }

            var builder = new StringBuilder("^");
            foreach (var ch in pattern)
            {
                switch (ch)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '?':
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(ch.ToString()));
                        break;
                }
            }

            builder.Append('$');
            var regex = new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Cache[pattern] = regex;
            return regex;
        }
    }
}
