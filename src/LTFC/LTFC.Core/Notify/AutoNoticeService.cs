using System;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// AutoNotice（内置模块）：读取 command.txt（默认 src\AutoNotice\command.txt），
    /// 按星期与时间弹出系统通知。到点只弹通知，不会强制置前任何窗口。
    /// </summary>
    internal sealed class AutoNoticeService : IDisposable
    {
        private readonly Timer _timer;
        private AutoNoticeConfig _config;
        private string _lastFiredKey = string.Empty;

        public AutoNoticeService()
        {
            _timer = new Timer { Interval = 15000 };
            _timer.Tick += (s, e) => Check();
        }

        /// <summary>到点触发（参数为提醒内容）。</summary>
        public event Action<AutoNoticeEntry> Triggered;

        public AutoNoticeConfig Config
        {
            get
            {
                if (_config == null) _config = AutoNoticeConfig.Load(AppPaths.AutoNoticeConfigFile);
                return _config;
            }
        }

        public bool Enabled { get { return Settings.AutoNoticeEnabled; } }
        /// <summary>重新读取配置（配置文件被编辑后调用）。</summary>
        public AutoNoticeConfig Reload()
        {
            _config = AutoNoticeConfig.Load(AppPaths.AutoNoticeConfigFile);
            AppLog.Info("AutoNotice 配置已重新加载：" + _config.Path
                        + "（有效 " + _config.Entries.Count + " 条，问题 " + _config.Errors.Count + " 条）");
            return _config;
        }

        public void Start()
        {
            Stop();
            if (!Settings.AutoNoticeEnabled)
            {
                AppLog.Info("AutoNotice 已停用，不启动定时检查。");
                return;
            }

            _timer.Start();
            AppLog.Info("AutoNotice 已启动，配置：" + Config.Path + "，共 " + Config.Entries.Count + " 条提醒。");
        }

        public void Stop()
        {
            try { _timer.Stop(); } catch (Exception) { }
        }

        public DateTime? NextDue() { return Config.NextDue(DateTime.Now); }

        /// <summary>下次提醒的可读描述。</summary>
        public string NextDueText()
        {
            if (!Settings.AutoNoticeEnabled) return "已停用";
            DateTime? next = NextDue();
            if (!next.HasValue) return "没有可用的提醒（请检查 command.txt）";

            TimeSpan span = next.Value - DateTime.Now;
            string prefix = span.TotalMinutes < 1 ? "马上" : ("约 " + (int)Math.Round(span.TotalMinutes) + " 分钟后");
            return next.Value.ToString("MM-dd HH:mm") + "（" + prefix + "）";
        }

        private void Check()
        {
            if (!Settings.AutoNoticeEnabled) return;

            AutoNoticeConfig config = Config;
            if (config.Entries.Count == 0) return;

            DateTime now = DateTime.Now;
            foreach (AutoNoticeEntry entry in config.Entries)
            {
                if (entry.Day.HasValue && now.DayOfWeek != entry.Day.Value) continue;
                if (now.Hour != entry.Hour || now.Minute != entry.Minute) continue;

                string key = now.ToString("yyyyMMdd") + "|" + entry.Raw;
                if (string.Equals(key, _lastFiredKey, StringComparison.Ordinal)) continue;
                _lastFiredKey = key;

                Raise(entry);
                break;
            }
        }

        /// <summary>立即测试一条提醒（不修改配置文件）。</summary>
        public void FireTest(string text)
        {
            Raise(new AutoNoticeEntry
            {
                Day = null,
                Hour = DateTime.Now.Hour,
                Minute = DateTime.Now.Minute,
                Text = string.IsNullOrEmpty(text) ? "这是一条测试提醒" : text,
                Raw = "测试",
                LineNumber = 0
            });
        }

        private void Raise(AutoNoticeEntry entry)
        {
            try
            {
                if (Settings.AutoNoticeSound)
                {
                    try { SystemSounds.Asterisk.Play(); } catch (Exception) { }
                }

                AppLog.Info("AutoNotice 触发提醒：" + entry.TimeText + " " + entry.Text);
                Action<AutoNoticeEntry> handler = Triggered;
                if (handler != null) handler(entry);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("触发定时提醒失败", ex);
            }
        }

        /// <summary>确保配置文件存在（第一次使用时写入模板）。</summary>
        public static void EnsureConfigFile()
        {
            try
            {
                string path = AppPaths.AutoNoticeConfigFile;
                if (File.Exists(path)) return;
                AppPaths.EnsureAutoNoticeDir();
                File.WriteAllText(path, AutoNoticeConfig.Template(), new System.Text.UTF8Encoding(true));
                AppLog.Info("已创建 AutoNotice 配置模板：" + path);
            }
            catch (Exception ex)
            {
                AppLog.Exception_("创建 AutoNotice 配置模板失败", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                _timer.Stop();
                _timer.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
