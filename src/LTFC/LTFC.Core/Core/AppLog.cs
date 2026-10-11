using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LTFC
{
    /// <summary>
    /// 简单文件日志：%APPDATA%\LTFC\logs\&lt;名字&gt;.txt。
    /// 各模块写各自的文件，互不干扰（子程序崩溃也不影响主程序日志）。
    /// </summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        /// <summary>主程序日志。</summary>
        public static string FilePath { get { return Path.Combine(AppPaths.LogDir, "ltfc.txt"); } }

        /// <summary>按名字取日志文件（模块日志）。</summary>
        public static string FileFor(string name)
        {
            string safe = string.IsNullOrEmpty(name) ? "ltfc" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(AppPaths.LogDir, safe + ".txt");
        }

        public static void Info(string message) { Write(FilePath, "INFO ", message); }
        public static void Warn(string message) { Write(FilePath, "WARN ", message); }
        public static void Error(string message) { Write(FilePath, "ERROR", message); }
        public static void Trace(string message) { Write(FilePath, "TRACE", message); }

        /// <summary>写指定日志文件。</summary>
        public static void InfoTo(string fileName, string message) { Write(fileName, "INFO ", message); }
        public static void WarnTo(string fileName, string message) { Write(fileName, "WARN ", message); }
        public static void ErrorTo(string fileName, string message) { Write(fileName, "ERROR", message); }

        public static void Exception_(string context, Exception ex)
        {
            if (ex == null) { Warn(context); return; }
            Error(context + " -> " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            if (ex.InnerException != null) Error("  inner: " + ex.InnerException.Message);
        }

        private static void Write(string path, string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        string tail = ReadTail(path, 128 * 1024);
                        File.WriteAllText(path, "[日志已截断]" + Environment.NewLine + tail, Encoding.UTF8);
                    }

                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1} {2}{3}",
                        DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 日志失败不影响主流程
            }
        }

        /// <summary>读取日志尾部（供“查看日志”使用）。</summary>
        public static string ReadTail(string path, int bytes)
        {
            try
            {
                if (!File.Exists(path)) return string.Empty;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, fs.Length - bytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[fs.Length - start];
                    int read = fs.Read(buffer, 0, buffer.Length);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>最近 N 行日志。</summary>
        public static string[] TailLines(string path, int lines)
        {
            string text = ReadTail(path, 256 * 1024);
            if (text.Length == 0) return new string[0];
            string[] all = text.Replace("\r\n", "\n").Split('\n');
            var list = new List<string>();
            for (int i = Math.Max(0, all.Length - lines); i < all.Length; i++)
            {
                if (all[i].Length > 0) list.Add(all[i]);
            }
            return list.ToArray();
        }
    }
}
