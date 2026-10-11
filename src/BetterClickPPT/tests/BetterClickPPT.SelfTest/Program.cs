using System.Text;

namespace BetterClickPPT.SelfTest;

/// <summary>
/// BetterClickPPT 自测程序：不依赖任何测试框架，直接运行即可。
/// 用法： BetterClickPPT.SelfTest.exe [--unit-only] [--e2e-only] [--no-install]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // 与主程序保持一致：按显示器 DPI 感知，保证模拟点击的坐标与钩子坐标在同一坐标系
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // 关闭控制台快速编辑：测试会向屏幕注入真实点击，避免误点控制台导致进程被“选择模式”冻结。
        DisableConsoleQuickEdit();

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 忽略控制台编码设置失败。
        }

        var unitOnly = args.Contains("--unit-only");
        var e2eOnly = args.Contains("--e2e-only");
        var skipInstall = args.Contains("--no-install");

        Console.WriteLine("==================================================");
        Console.WriteLine(" BetterClickPPT 自测程序");
        Console.WriteLine("==================================================");
        Console.WriteLine($"程序目录：{AppContext.BaseDirectory}");
        Console.WriteLine($"操作系统：{Environment.OSVersion.VersionString}（{(Environment.Is64BitProcess ? "64 位进程" : "32 位进程")}）");
        Console.WriteLine();

        if (!e2eOnly)
        {
            Console.WriteLine("--- 配置与匹配 ---");
            TestRunner.Run("通配符匹配", UnitTestsConfig.WildcardMatching);
            TestRunner.Run("配置读写（含中文）", UnitTestsConfig.ConfigRoundTrip);
            TestRunner.Run("配置修复与收敛", UnitTestsConfig.ConfigNormalizeRepairs);
            TestRunner.Run("轻触动作默认值与克隆", UnitTestsConfig.TouchActionDefaults);
            TestRunner.Run("鼠标点击坐标换算（含多显示器 / 高 DPI）", UnitTestsConfig.MouseClickCoordinateNormalization);
            TestRunner.Run("动作窗口：动作在消息循环中执行（不在钩子内）", UnitTestsConfig.DeferredActionWindow);
            TestRunner.Run("读取安装状态", UnitTestsConfig.InstallStateRead);

            Console.WriteLine();
            Console.WriteLine("--- 放映开始时自动切回鼠标模式 ---");
            TestRunner.Run("配置：开关的默认值 / 克隆 / 往返读写", UnitTestsTouchMode.ConfigToggle);
            TestRunner.Run("增强器：把该开关接到触摸模式守护", UnitTestsTouchMode.EnhancerWiring);
            TestRunner.Run("守护：关闭或目标无效时不动作", UnitTestsTouchMode.GuardStaysIdleWhenDisabled);
            TestRunner.Run("守护：先失败后重试成功", UnitTestsTouchMode.GuardRetriesThenSucceeds);
            TestRunner.Run("守护：多次失败后放弃（有次数上限）", UnitTestsTouchMode.GuardGivesUpAfterMaxAttempts);
            TestRunner.Run("守护：切换实现抛异常也不影响主流程", UnitTestsTouchMode.GuardSwallowsSwitcherExceptions);
            TestRunner.Run("守护：放映结束会取消未完成的尝试", UnitTestsTouchMode.GuardCancelsWhenShowEnds);
            TestRunner.Run("只对 Office 放映窗口执行切换", UnitTestsTouchMode.OfficeShowDetection);
            TestRunner.Run("模式判定与选中项推断", UnitTestsTouchMode.ModesAndSelectionClassification);
            TestRunner.Run("「触摸 / 鼠标模式」按钮只读探测", UnitTestsTouchMode.ToggleLookupSmoke);

            Console.WriteLine();
            Console.WriteLine("--- 放映窗口识别 ---");
            TestRunner.Run("规则匹配（PowerPoint / WPS / 自定义）", UnitTestsSlideshow.SlideshowRuleMatching);
            TestRunner.Run("目标软件自动检测", UnitTestsSlideshow.OfficeAppDetection);

            Console.WriteLine();
            Console.WriteLine("--- 输入识别 ---");
            TestRunner.Run("轻触 / 长按 / 滑动 / 多指 判定", UnitTestsInput.TapGestureDetection);
            TestRunner.Run("触摸与鼠标来源判定", UnitTestsInput.InputSourceClassification);
            TestRunner.Run("翻页按键映射", UnitTestsInput.KeyResolution);
            TestRunner.Run("深度触控模式启动（无触摸屏时可跳过）", UnitTestsInput.RawTouchSourceSmoke);
        }

        if (!unitOnly)
        {
            Console.WriteLine();
            Console.WriteLine("--- 端到端测试（真实鼠标钩子 + 模拟放映窗口）---");
            TestRunner.Run("触摸点击翻页且原始点击被吞掉", EndToEndTests.TouchClickAdvancesAndSwallows);
            TestRunner.Run("防抖：连续两次点击只翻一页", EndToEndTests.DebouncePreventsDoubleAdvance);
            TestRunner.Run("关闭吞掉后原始点击放行", EndToEndTests.SwallowDisabledPassesClickThrough);
            TestRunner.Run("PostMessage 方式翻页", EndToEndTests.PostMessageModeAdvances);
            TestRunner.Run("不匹配的窗口完全不受影响", EndToEndTests.UnmatchedProcessIsNotTouched);
            TestRunner.Run("真实全屏窗口识别与翻页", EndToEndTests.FullScreenWindowIsDetectedAndAdvances);
            TestRunner.Run("普通注入点击被忽略", EndToEndTests.InjectedPlainClickIsIgnored);
            TestRunner.Run("触摸轻触还原为鼠标点击（位置精确）", EndToEndTests.TouchTapIsReplayedAsMouseClick);
            TestRunner.Run("左侧轻触的点击位置保持（上一页按钮可用）", EndToEndTests.TouchTapOnLeftSideKeepsPosition);

            if (!skipInstall)
            {
                Console.WriteLine();
                Console.WriteLine("--- 安装 / 卸载 ---");
                TestRunner.Run("安装 → 升级 → 卸载（临时目录）", InstallTests.InstallAndUninstallRoundTrip);
            }
        }

        var exitCode = TestRunner.Summarize();

        // 若存在“卡在 SendInput 里”的后台线程（桌面输入队列被占用），
        // Environment.Exit 可保证进程立即结束，不等待该线程。
        Console.Out.Flush();
        Environment.Exit(exitCode);
        return exitCode;
    }

    /// <summary>
    /// 关闭控制台“快速编辑”模式：注入真实点击时若点到控制台窗口，会让进程进入选择状态而卡住。
    /// 没有控制台或调用失败时忽略。
    /// </summary>
    private static void DisableConsoleQuickEdit()
    {
        const int StdInputHandle = -10;
        const uint EnableQuickEditMode = 0x0040;

        try
        {
            var handle = GetStdHandle(StdInputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return;
            }

            if (!GetConsoleMode(handle, out var mode))
            {
                return;
            }

            if ((mode & EnableQuickEditMode) != 0)
            {
                SetConsoleMode(handle, mode & ~EnableQuickEditMode);
            }
        }
        catch
        {
            // 忽略（例如被重定向到文件时没有控制台）。
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
