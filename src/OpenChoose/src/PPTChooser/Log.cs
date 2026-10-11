using System;
using System.IO;
using System.Text;

namespace OpenChoose
{
    /// <summary>
    /// 极简日志（%AppData%\OpenChoose\log.txt），便于排查问题。
    /// </summary>
    internal static class Log
    {
        public static string FilePath
        {
            get { return Path.Combine(AppConfig.DirectoryPath, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.DirectoryPath);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }

        public static void Write(string message, Exception error)
        {
            Write(message + " -> " + error.GetType().Name + ": " + error.Message);
        }
    }
}
