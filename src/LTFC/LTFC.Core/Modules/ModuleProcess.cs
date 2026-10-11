using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace LTFC
{
    internal enum ModuleState
    {
        NotConfigured,
        Stopped,
        Running,
        Failed
    }

    internal sealed class ModuleStateEventArgs : EventArgs
    {
        public ModuleStateEventArgs(ModuleState state, string message)
        {
            State = state;
            Message = message;
        }

        public ModuleState State { get; private set; }
        public string Message { get; private set; }
    }

    /// <summary>
    /// 一个子程序的进程管理：启动、停止、崩溃识别、状态文件读取。
    /// 子程序是独立进程，拥有自己的异常处理与日志；这里只负责“看护”，不共享 UI 状态。
    /// </summary>
    internal sealed class ModuleProcess : IDisposable
    {
        private readonly ModuleDefinition _definition;
        private readonly object _gate = new object();
        private Process _process;
        private ModuleState _state = ModuleState.Stopped;
        private string _lastMessage = string.Empty;
        private int _exitCode;
        private DateTime? _startedAt;

        public ModuleProcess(ModuleDefinition definition)
        {
            _definition = definition;
        }

        public ModuleDefinition Definition { get { return _definition; } }

        public event EventHandler<ModuleStateEventArgs> StateChanged;

        public ModuleState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public string LastMessage
        {
            get
            {
                lock (_gate)
                {
                    return _lastMessage;
                }
            }
        }

        public int LastExitCode
        {
            get
            {
                lock (_gate)
                {
                    return _exitCode;
                }
            }
        }

        public DateTime? StartedAt
        {
            get
            {
                lock (_gate)
                {
                    return _startedAt;
                }
            }
        }

        /// <summary>解析后的可执行文件路径（内置目录 / modules 子目录 / 备用路径 / 用户自定义）。</summary>
        public string ExecutablePath
        {
            get { return Modules.ResolveExecutablePath(_definition); }
        }

        /// <summary>文件是否存在于解析出的路径。</summary>
        public bool Exists
        {
            get
            {
                try
                {
                    string path = ExecutablePath;
                    return !string.IsNullOrEmpty(path) && File.Exists(path);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    try
                    {
                        return _process != null && !_process.HasExited;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// 启动子程序（新开一个独立窗口）。
        /// 若它已经在运行，则按“单实例”处理：只显示已有窗口，不再启动第二个进程。
        /// </summary>
        public bool Start(out string message)
        {
            if (IsRunning)
            {
                string activateMessage;
                bool activated = ActivateWindow(out activateMessage);
                message = activated
                    ? _definition.DisplayName + " 已在运行，已显示它已有的窗口。"
                    : _definition.DisplayName + " 已在运行（" + activateMessage + "）。";
                return true;
            }

            if (!Settings.ModuleEnabled(_definition.Id))
            {
                message = _definition.DisplayName + " 已在设置中禁用。";
                SetState(ModuleState.Stopped, message);
                return false;
            }

            string path = ExecutablePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                message = "找不到 " + _definition.DisplayName + " 的程序文件：" + Environment.NewLine + path;
                SetState(ModuleState.NotConfigured, message);
                return false;
            }

            string args = (string.IsNullOrEmpty(_argumentOverride) ? (_definition.Arguments ?? string.Empty) : _argumentOverride)
                .Replace("{parent}", Process.GetCurrentProcess().Id.ToString());
            if (!string.IsNullOrEmpty(_definition.StatusFile))
            {
                args = (args + " --status-file \"" + _definition.StatusFile + "\"").Trim();
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            Process process;

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? AppPaths.ExeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendLine(stdout, e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendLine(stderr, e.Data); };
                process.Exited += (s, e) => OnProcessExited(process, stdout, stderr);

                if (!process.Start())
                {
                    message = "启动 " + _definition.DisplayName + " 失败（进程未启动）。";
                    SetState(ModuleState.Failed, message);
                    AppLog.ErrorTo(_definition.LogName, message + " 路径：" + path);
                    return false;
                }

                try
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch (Exception)
                {
                }

                lock (_gate)
                {
                    _process = process;
                    _startedAt = DateTime.Now;
                }

                AppLog.InfoTo(_definition.LogName, "已启动：" + path + " " + args);
            }
            catch (Exception ex)
            {
                message = "启动 " + _definition.DisplayName + " 失败：" + ex.Message;
                SetState(ModuleState.Failed, message);
                AppLog.Exception_("启动子程序失败：" + _definition.DisplayName, ex);
                return false;
            }

            // 启动确认：存活超过约 0.6 秒即认为启动成功；立刻退出则视为启动失败（例如缺少运行库）。
            bool alive = false;
            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(80);
                alive = IsRunning;
                if (!alive) break;
            }

            if (!alive)
            {
                int code = LastExitCode;
                message = _definition.DisplayName + " 启动后立即退出（退出码 " + code + "）。"
                          + Environment.NewLine + "详见日志：" + AppLog.FileFor(_definition.LogName);
                SetState(ModuleState.Failed, message);
                AppLog.ErrorTo(_definition.LogName, message);
                return false;
            }

            message = _definition.DisplayName + " 已启动。";
            SetState(ModuleState.Running, message);
            return true;
        }

        /// <summary>
        /// 显示该模块已有的窗口（不新开进程）。
        /// 若 LTFC 自己没有记录到进程（例如 LTFC 重启过），会先在系统里找回同路径的实例并接管。
        /// </summary>
        public bool ActivateWindow(out string message)
        {
            Process process = EnsureProcessReference();
            if (process == null)
            {
                message = "没有找到正在运行的程序窗口";
                return false;
            }

            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    message = "程序已经退出";
                    return false;
                }

                IntPtr handle = IntPtr.Zero;
                try { handle = process.MainWindowHandle; } catch (Exception) { }

                // 托盘常驻的程序有时拿不到 MainWindowHandle，按进程 ID 再找一遍
                for (int i = 0; i < 8 && handle == IntPtr.Zero; i++)
                {
                    Thread.Sleep(70);
                    process.Refresh();
                    try { handle = process.MainWindowHandle; } catch (Exception) { }
                    if (handle == IntPtr.Zero) handle = Native.FindTopLevelWindowForProcess(process.Id);
                }

                if (handle != IntPtr.Zero && Native.ActivateWindowOfProcess(process.Id, handle))
                {
                    SetState(ModuleState.Running, _definition.DisplayName + " 已显示窗口。");
                    AppLog.InfoTo(_definition.LogName, "已显示已有窗口：" + Native.WindowTitle(handle));
                    message = "已显示已有窗口";
                    return true;
                }

                message = _definition.DisplayName + " 正在后台运行（没有可见窗口，可能驻留在托盘）";
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Exception_("显示子程序窗口失败：" + _definition.DisplayName, ex);
                message = "显示窗口失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>取当前进程引用；LTFC 重启后可从系统中找回同路径的实例（避免重复启动）。</summary>
        private Process EnsureProcessReference()
        {
            lock (_gate)
            {
                if (_process != null && !SafeHasExited(_process)) return _process;
            }

            string expected = ExecutablePath;
            if (string.IsNullOrEmpty(expected)) return null;

            try
            {
                string name = Path.GetFileNameWithoutExtension(expected);
                if (string.IsNullOrEmpty(name)) return null;

                foreach (Process candidate in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = null;
                        try { path = candidate.MainModule != null ? candidate.MainModule.FileName : null; }
                        catch (Exception) { }

                        // 读不到路径时（权限不足）也接受同名进程
                        if (path != null && !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)) continue;

                        lock (_gate)
                        {
                            _process = candidate;
                            _startedAt = TryStartTime(candidate);
                        }

                        SetState(ModuleState.Running, _definition.DisplayName + " 已在运行（已接管）。");
                        AppLog.InfoTo(_definition.LogName, "在系统中找到正在运行的实例，接管进程 " + candidate.Id);
                        return candidate;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("查找正在运行的子程序失败：" + _definition.DisplayName, ex);
            }

            return null;
        }

        private static DateTime? TryStartTime(Process process)
        {
            try { return process.StartTime; }
            catch (Exception) { return null; }
        }

        /// <summary>是否存在已经在运行的实例（LTFC 重启后也能发现）。</summary>
        public bool HasExternalInstance()
        {
            return EnsureProcessReference() != null;
        }

        /// <summary>用额外的启动参数启动（例如打开子程序自己的设置界面）。</summary>
        public bool StartWithArguments(string arguments, out string message)
        {
            _argumentOverride = arguments;
            try
            {
                return Start(out message);
            }
            finally
            {
                _argumentOverride = null;
            }
        }

        private string _argumentOverride;

        /// <summary>停止子程序：先礼貌关闭窗口，超时后结束进程。</summary>
        public bool Stop(out string message)
        {
            Process process;
            lock (_gate)
            {
                process = _process;
            }

            if (process == null)
            {
                message = _definition.DisplayName + " 未在运行。";
                return true;
            }

            try
            {
                _stopping = true;
                if (!SafeHasExited(process))
                {
                    try { process.CloseMainWindow(); }
                    catch (Exception) { }

                    if (!process.WaitForExit(2500))
                    {
                        try { process.Kill(); }
                        catch (Exception ex) { AppLog.Exception_("结束子程序失败：" + _definition.DisplayName, ex); }
                        process.WaitForExit(2000);
                    }
                }

                message = _definition.DisplayName + " 已停止。";
                AppLog.InfoTo(_definition.LogName, message);
                SetState(ModuleState.Stopped, message);
                return true;
            }
            catch (Exception ex)
            {
                message = "停止 " + _definition.DisplayName + " 失败：" + ex.Message;
                AppLog.Exception_("停止子程序失败：" + _definition.DisplayName, ex);
                return false;
            }
        }

        private volatile bool _stopping;

        /// <summary>读取子程序写入的状态文件（key=value 文本；没有则返回空）。</summary>
        public IDictionary<string, string> ReadStatusFile()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = _definition.StatusFile;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return map;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == ';' || text[0] == '#') continue;
                    int index = text.IndexOf('=');
                    if (index <= 0) continue;
                    map[text.Substring(0, index).Trim()] = text.Substring(index + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception_("读取子程序状态失败：" + _definition.DisplayName, ex);
            }
            return map;
        }

        /// <summary>刷新状态（进程被外部结束 / 崩溃时同步到界面）。</summary>
        public ModuleState Refresh()
        {
            Process process;
            lock (_gate)
            {
                process = _process;
            }

            if (State == ModuleState.Running && (process == null || SafeHasExited(process)))
            {
                int code = SafeExitCode(process);
                if (_stopping || code == 0)
                {
                    SetState(ModuleState.Stopped, _definition.DisplayName + " 已退出。");
                }
                else
                {
                    SetState(ModuleState.Failed, _definition.DisplayName + " 异常退出（退出码 " + code + "），详见日志："
                                               + AppLog.FileFor(_definition.LogName));
                }
            }

            return State;
        }

        private void OnProcessExited(Process process, StringBuilder stdout, StringBuilder stderr)
        {
            int code = SafeExitCode(process);
            bool stopping = _stopping;
            _stopping = false;

            lock (_gate)
            {
                _exitCode = code;
                if (ReferenceEquals(_process, process)) _process = null;
                _startedAt = null;
            }

            string outText = stdout.ToString().Trim();
            string errText = stderr.ToString().Trim();
            if (outText.Length > 0) AppLog.InfoTo(_definition.LogName, "标准输出：" + outText);
            if (errText.Length > 0) AppLog.ErrorTo(_definition.LogName, "标准错误：" + errText);

            if (stopping || code == 0)
            {
                SetState(ModuleState.Stopped, _definition.DisplayName + " 已退出。");
            }
            else
            {
                SetState(ModuleState.Failed, _definition.DisplayName + " 异常退出（退出码 " + code + "），详见日志："
                                           + AppLog.FileFor(_definition.LogName));
            }
        }

        private static void AppendLine(StringBuilder builder, string line)
        {
            lock (builder)
            {
                if (builder.Length > 8192) builder.Clear();
                builder.AppendLine(line);
            }
        }

        private static bool SafeHasExited(Process process)
        {
            try { return process == null || process.HasExited; }
            catch (Exception) { return true; }
        }

        private static int SafeExitCode(Process process)
        {
            try
            {
                if (process != null && process.HasExited) return process.ExitCode;
            }
            catch (Exception)
            {
            }
            return -1;
        }

        private void SetState(ModuleState state, string message)
        {
            lock (_gate)
            {
                _state = state;
                _lastMessage = message ?? string.Empty;
            }

            EventHandler<ModuleStateEventArgs> handler = StateChanged;
            if (handler == null) return;
            try
            {
                handler(this, new ModuleStateEventArgs(state, message));
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_process != null)
                {
                    try { _process.Dispose(); } catch (Exception) { }
                    _process = null;
                }
            }
        }
    }
}
