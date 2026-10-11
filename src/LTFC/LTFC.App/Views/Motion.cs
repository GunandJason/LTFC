using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// 轻量动效工具（只在界面线程跑）。
    /// 设计要点：
    /// · 用 WinForms Timer 按“真实经过时间”计算进度，即使某一帧被拖慢也能准时结束；
    /// · 任何时候出异常、窗口被释放或已隐藏，立刻跳到终态，绝不让动画把界面卡住；
    /// · 系统禁用了动画（远程桌面 / 辅助功能设置）时自动不播放，直接给终态。
    /// </summary>
    internal static class Motion
    {
        /// <summary>窗口打开动效时长（毫秒）。</summary>
        public const int OpenDurationMs = 220;

        /// <summary>总开关：自测 / 特殊场景可置为 false，所有动效立即完成。</summary>
        public static bool Enabled = true;

        /// <summary>系统是否禁用了动画效果。</summary>
        public static bool SystemReducedMotion
        {
            get
            {
                try
                {
                    return !SystemInformation.IsMenuAnimationEnabled || !SystemInformation.UIEffectsEnabled;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>当前是否可以播放动效。</summary>
        public static bool Available
        {
            get { return Enabled && !SystemReducedMotion; }
        }

        /// <summary>
        /// 窗口打开动效：淡入 + 轻微下移到位。
        /// <paramref name="onCompleted"/> 一定会在动效结束（或跳过）后执行一次，
        /// 因此可以把“启动子程序”这类耗时工作放到动效之后再跑，界面能先出现。
        /// </summary>
        public static void PlayOpen(Form form, Action onCompleted)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated || !Available)
            {
                Invoke(onCompleted);
                return;
            }

            double startOpacity = Math.Max(0.25d, Math.Min(1d, form.Opacity));
            int targetTop = form.Top;
            int fromTop = targetTop - Theme.S(16);
            bool finished = false;
            long startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                form.Opacity = Math.Min(startOpacity, 0.35d);
                form.Top = fromTop;
            }
            catch (Exception)
            {
                Invoke(onCompleted);
                return;
            }

            var timer = new Timer { Interval = 15 };

            Action finish = delegate
            {
                if (finished) return;
                finished = true;

                try
                {
                    timer.Stop();
                    timer.Dispose();
                }
                catch (Exception)
                {
                }

                try
                {
                    if (!form.IsDisposed)
                    {
                        // 无论动效是否被打断，都恢复到最终状态
                        form.Opacity = 1d;
                        form.Top = targetTop;
                    }
                }
                catch (Exception)
                {
                }

                Invoke(onCompleted);
            };

            timer.Tick += delegate
            {
                try
                {
                    if (form.IsDisposed || !form.Visible)
                    {
                        finish();
                        return;
                    }

                    double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
                    double progress = elapsedMs / OpenDurationMs;
                    if (progress >= 1d)
                    {
                        finish();
                        return;
                    }

                    // 缓出曲线：开头快、结尾稳
                    double eased = 1d - Math.Pow(1d - progress, 3d);
                    form.Opacity = startOpacity + (1d - startOpacity) * eased;
                    form.Top = (int)Math.Round(fromTop + (targetTop - fromTop) * eased);
                }
                catch (Exception)
                {
                    finish();
                }
            };

            timer.Start();
        }

        private static void Invoke(Action action)
        {
            if (action == null) return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Exception_("执行动效回调失败", ex);
            }
        }
    }
}
