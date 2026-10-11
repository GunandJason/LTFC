using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LTFC
{
    /// <summary>一条定时提醒（来自 command.txt 的一行）。</summary>
    internal sealed class AutoNoticeEntry
    {
        /// <summary>星期（null = 每天）。</summary>
        public DayOfWeek? Day { get; set; }

        public int Hour { get; set; }

        public int Minute { get; set; }

        public string Text { get; set; }

        /// <summary>原始行（便于在界面上原样显示）。</summary>
        public string Raw { get; set; }

        /// <summary>行号（从 1 开始，便于定位错误）。</summary>
        public int LineNumber { get; set; }

        public string DayText { get { return Day.HasValue ? WeekText(Day.Value) : "每天"; } }

        public string TimeText { get { return Hour.ToString("00") + ":" + Minute.ToString("00"); } }

        public static string WeekText(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }
    }

    /// <summary>
    /// command.txt 的解析结果。格式：每行一条“星期 时间 内容”。
    /// 星期：1-7（1=周一 … 7=周日）、0 或 * 表示每天，也可写 周一/一/Mon。
    /// 时间：HH:mm（也接受 H:mm 与 HHmm）。以 # 或 ; 开头的行是注释。
    /// 某一行写错只跳过该行，不影响其他提醒。
    /// </summary>
    internal sealed class AutoNoticeConfig
    {
        public AutoNoticeConfig()
        {
            Entries = new List<AutoNoticeEntry>();
            Errors = new List<string>();
        }

        public IList<AutoNoticeEntry> Entries { get; private set; }

        /// <summary>解析失败的行说明。</summary>
        public IList<string> Errors { get; private set; }

        public string Path { get; set; }

        public bool Exists { get; set; }

        public static AutoNoticeConfig Load(string path)
        {
            var config = new AutoNoticeConfig { Path = path };
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return config;
                config.Exists = true;

                int lineNumber = 0;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    lineNumber++;
                    ParseLine(config, raw, lineNumber);
                }
            }
            catch (Exception ex)
            {
                config.Errors.Add("读取配置失败：" + ex.Message);
                AppLog.Exception_("读取 AutoNotice 配置失败", ex);
            }

            return config;
        }

        public static void ParseLine(AutoNoticeConfig config, string raw, int lineNumber)
        {
            string line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) return;
            if (line[0] == '#' || line[0] == ';' || line[0] == '/' || line[0] == '[') return;

            string[] parts = line.Split(new[] { ' ', '\t', '，', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                config.Errors.Add("第 " + lineNumber + " 行：内容不完整（应为“星期 时间 内容”）");
                return;
            }

            DayOfWeek? day;
            if (!TryParseDay(parts[0], out day))
            {
                config.Errors.Add("第 " + lineNumber + " 行：无法识别星期“" + parts[0] + "”");
                return;
            }

            int hour, minute;
            if (!TryParseTime(parts[1], out hour, out minute))
            {
                config.Errors.Add("第 " + lineNumber + " 行：无法识别时间“" + parts[1] + "”");
                return;
            }

            string text = string.Join(" ", parts, 2, parts.Length - 2).Trim();
            if (text.Length == 0)
            {
                config.Errors.Add("第 " + lineNumber + " 行：提醒内容为空");
                return;
            }

            config.Entries.Add(new AutoNoticeEntry
            {
                Day = day,
                Hour = hour,
                Minute = minute,
                Text = text,
                Raw = line,
                LineNumber = lineNumber
            });
        }

        private static bool TryParseDay(string token, out DayOfWeek? day)
        {
            day = null;
            if (string.IsNullOrEmpty(token)) return false;

            string value = token.Trim();
            if (value == "*" || value == "＊" || value == "0" || value == "每天" || value == "每日") return true;

            int number;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                if (number < 1 || number > 7) return false;
                day = number == 7 ? DayOfWeek.Sunday : (DayOfWeek)number;
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "mon": case "monday": case "周一": case "星期一": case "一": day = DayOfWeek.Monday; return true;
                case "tue": case "tuesday": case "周二": case "星期二": case "二": day = DayOfWeek.Tuesday; return true;
                case "wed": case "wednesday": case "周三": case "星期三": case "三": day = DayOfWeek.Wednesday; return true;
                case "thu": case "thursday": case "周四": case "星期四": case "四": day = DayOfWeek.Thursday; return true;
                case "fri": case "friday": case "周五": case "星期五": case "五": day = DayOfWeek.Friday; return true;
                case "sat": case "saturday": case "周六": case "星期六": case "六": day = DayOfWeek.Saturday; return true;
                case "sun": case "sunday": case "周日": case "星期日": case "日": case "天": case "七": day = DayOfWeek.Sunday; return true;
            }

            return false;
        }

        private static bool TryParseTime(string token, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            if (string.IsNullOrEmpty(token)) return false;

            string value = token.Trim().Replace("：", ":");
            int colon = value.IndexOf(':');
            string hourText = value;
            string minuteText = "0";

            if (colon >= 0)
            {
                hourText = value.Substring(0, colon);
                minuteText = value.Substring(colon + 1);
            }
            else if (value.Length == 4)
            {
                hourText = value.Substring(0, 2);
                minuteText = value.Substring(2, 2);
            }
            else if (value.Length > 2)
            {
                return false;
            }

            if (!int.TryParse(hourText, out hour) || !int.TryParse(minuteText, out minute)) return false;
            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }

        /// <summary>下一次提醒时间（没有可用提醒时返回 null）。</summary>
        public DateTime? NextDue(DateTime now)
        {
            DateTime? bestTime = null;
            foreach (AutoNoticeEntry entry in Entries)
            {
                for (int offset = 0; offset <= 7; offset++)
                {
                    DateTime day = now.Date.AddDays(offset);
                    if (entry.Day.HasValue && day.DayOfWeek != entry.Day.Value) continue;

                    var candidate = new DateTime(day.Year, day.Month, day.Day, entry.Hour, entry.Minute, 0);
                    if (candidate <= now) continue;
                    if (!bestTime.HasValue || candidate < bestTime.Value) bestTime = candidate;
                    break;
                }
            }

            return bestTime;
        }

        /// <summary>新建配置文件时使用的模板内容。</summary>
        public static string Template()
        {
            var lines = new List<string>
            {
                "# AutoNotice 定时提醒配置（command.txt）",
                "# 每行一条：星期 时间 提醒内容",
                "# 星期：1=周一 2=周二 3=周三 4=周四 5=周五 6=周六 7=周日；0 或 * 表示每天",
                "# 时间：24 小时制 HH:mm",
                "# 以 # 或 ; 开头的行是注释；某一行写错只会跳过该行，不影响其他提醒",
                string.Empty,
                "1 08:00 早读开始",
                "1 12:00 午休",
                "5 15:30 大扫除",
                "* 17:30 提醒学生带走物品",
                string.Empty
            };
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }
}
