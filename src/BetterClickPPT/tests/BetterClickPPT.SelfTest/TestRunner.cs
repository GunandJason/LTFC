namespace BetterClickPPT.SelfTest;

/// <summary>轻量测试运行器（不依赖任何测试框架，直接运行即可）。</summary>
internal static class TestRunner
{
    private static readonly List<(string Name, bool Passed, string? Message)> Results = new();
    private static int _index;

    /// <summary>运行一个测试用例。</summary>
    public static void Run(string name, Action test)
    {
        _index++;
        var title = $"#{_index:00} {name}";

        try
        {
            test();
            Results.Add((title, true, null));
            Console.WriteLine($"  [通过] {title}");
        }
        catch (SkipTestException ex)
        {
            Results.Add((title, true, "跳过：" + ex.Message));
            Console.WriteLine($"  [跳过] {title} → {ex.Message}");
        }
        catch (Exception ex)
        {
            Results.Add((title, false, ex.Message));
            Console.WriteLine($"  [失败] {title}");
            Console.WriteLine($"          {ex.Message}");
        }
    }

    /// <summary>输出总结并返回退出码。</summary>
    public static int Summarize()
    {
        var failed = Results.Count(r => !r.Passed);
        var skipped = Results.Count(r => r.Passed && r.Message is not null);

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"总计 {Results.Count} 项，通过 {Results.Count - failed - skipped}，跳过 {skipped}，失败 {failed}");
        Console.WriteLine("==================================================");

        foreach (var (name, passed, message) in Results.Where(r => !r.Passed))
        {
            Console.WriteLine($"失败：{name}");
            Console.WriteLine($"      {message}");
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>断言为真。</summary>
    public static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>断言相等。</summary>
    public static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}（期望 {expected}，实际 {actual}）");
        }
    }

    /// <summary>断言为 null。</summary>
    public static void AssertNull(object? value, string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>断言不为 null 并返回。</summary>
    public static T AssertNotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }

        return value;
    }

    /// <summary>跳过当前测试。</summary>
    public static void Skip(string reason) => throw new SkipTestException(reason);

    /// <summary>表示测试被跳过。</summary>
    public sealed class SkipTestException : Exception
    {
        public SkipTestException(string message)
            : base(message)
        {
        }
    }
}
