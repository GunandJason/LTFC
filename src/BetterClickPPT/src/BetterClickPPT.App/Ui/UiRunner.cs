namespace BetterClickPPT.App.Ui;

/// <summary>
/// 在 UI 线程上“边跑耗时任务边刷新界面”的辅助方法
/// （安装 / 卸载会阻塞当前线程，这里用消息泵保持界面响应与进度刷新）。
/// </summary>
internal static class UiRunner
{
    /// <summary>执行耗时任务，期间持续泵消息（保持界面可重绘、进度可更新）。</summary>
    public static T RunWithPump<T>(Func<T> work, Action? onTick = null)
    {
        var task = System.Threading.Tasks.Task.Run(work);

        while (!task.IsCompleted)
        {
            Application.DoEvents();
            onTick?.Invoke();
            Thread.Sleep(15);
        }

        Application.DoEvents();

        if (task.IsFaulted)
        {
            var inner = task.Exception?.InnerException;
            throw inner ?? new InvalidOperationException("后台任务失败。");
        }

        return task.Result;
    }
}
