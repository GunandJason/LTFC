using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LTFC
{
    /// <summary>ClassTell 的一条消息（消息 / 过期）。</summary>
    internal sealed class ClassTellMessage
    {
        public string Time { get; set; }

        public string Kind { get; set; }

        public string Text { get; set; }

        public string Line { get { return (Time ?? string.Empty) + "  " + (Text ?? string.Empty); } }
    }

    /// <summary>ClassTell 与 LTFC 之间的数据（从交换文件读取）。</summary>
    internal sealed class ClassTellFeed
    {
        public ClassTellFeed()
        {
            Messages = new List<ClassTellMessage>();
            Expired = new List<ClassTellMessage>();
            Notes = new List<string>();
        }

        public IList<ClassTellMessage> Messages { get; private set; }

        public IList<ClassTellMessage> Expired { get; private set; }

        public List<string> Notes { get; private set; }

        public string Path { get; set; }

        public bool Exists { get; set; }

        public string Updated { get; set; }

        public string Account { get; set; }

        public string WindowState { get; set; }

        public bool HasData { get { return Messages.Count > 0 || Expired.Count > 0; } }
    }

    /// <summary>
    /// 读取 ClassTell 发布的数据交换文件（ClassTell 运行时写出，LTFC 只读）。
    /// 文件：%APPDATA%\LTFC\classtell-feed.txt
    /// 格式：
    ///   updated=2026-10-01 12:30:00
    ///   account=someone@outlook.com
    ///   window=running
    ///   [messages]
    ///   2026-10-01 12:30:00[TAB]呼叫[TAB]C 类邮件标题或摘要
    ///   [expired]
    ///   2026-09-30 08:05:00[TAB]未读[TAB]...
    /// 找不到文件时返回空数据，界面会提示“等待 ClassTell 提供数据”。
    /// </summary>
    internal static class ClassTellBridge
    {
        /// <summary>交换文件路径（与 ClassTell 约定固定位置）。</summary>
        public static string FeedFile { get { return Path.Combine(AppPaths.DataDir, "classtell-feed.txt"); } }

        public static ClassTellFeed Read()
        {
            var feed = new ClassTellFeed { Path = FeedFile };

            try
            {
                if (!File.Exists(FeedFile)) return feed;
                feed.Exists = true;

                var current = feed.Messages;
                foreach (string raw in File.ReadAllLines(FeedFile, Encoding.UTF8))
                {
                    string line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == ';' || line[0] == '#') continue;

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        if (section == "expired" || section == "stale" || section == "过期") current = feed.Expired;
                        else current = feed.Messages;
                        continue;
                    }

                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                        string value = line.Substring(equals + 1).Trim();
                        if (key == "updated") feed.Updated = value;
                        else if (key == "account") feed.Account = value;
                        else if (key == "window" || key == "state") feed.WindowState = value;
                        continue;
                    }

                    current.Add(ParseEntry(line));
                }
            }
            catch (Exception ex)
            {
                feed.Notes.Add("读取 ClassTell 数据失败：" + ex.Message);
                AppLog.Exception_("读取 ClassTell 数据失败", ex);
            }

            return feed;
        }

        private static ClassTellMessage ParseEntry(string line)
        {
            string[] parts = line.Split(new[] { '\t', '|' }, 3);
            if (parts.Length >= 3)
            {
                return new ClassTellMessage { Time = parts[0].Trim(), Kind = parts[1].Trim(), Text = parts[2].Trim() };
            }

            if (parts.Length == 2)
            {
                return new ClassTellMessage { Time = parts[0].Trim(), Kind = string.Empty, Text = parts[1].Trim() };
            }

            return new ClassTellMessage { Time = string.Empty, Kind = string.Empty, Text = line };
        }

        /// <summary>把数据整理成多行文字（用于日志与详情展示）。</summary>
        public static string Describe(IList<ClassTellMessage> list, string emptyText)
        {
            if (list == null || list.Count == 0) return emptyText;

            var builder = new StringBuilder();
            foreach (ClassTellMessage message in list)
            {
                builder.AppendLine(message.Line);
            }
            return builder.ToString().TrimEnd();
        }
    }
}
