using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OpenChoose
{
    /// <summary>“设为默认打开方式”的结果。</summary>
    internal sealed class SetDefaultResult
    {
        public bool Registered;
        public bool LegacyWritten;
        public readonly List<string> ProtectedExtensions = new List<string>();

        /// <summary>是否需要引导用户到 Windows 设置中选择默认应用。</summary>
        public bool NeedsSettings
        {
            get { return this.ProtectedExtensions.Count > 0; }
        }
    }

    /// <summary>
    /// 文件关联管理（全部写在 HKCU 下，不需要管理员权限）：
    /// 1) 注册 ProgID 与 Applications\PPTChooser.exe，让本程序出现在“打开方式”列表里；
    /// 2) 尝试把 .ppt/.pptx 等的默认打开方式指向本程序；
    ///    Windows 10/11 中若系统已写入受保护的 UserChoice（哈希校验），则跳过并引导用户到系统设置。
    /// </summary>
    internal static class FileAssociation
    {
        private const string ClassesPath = @"Software\Classes";
        private const string FileExtsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";
        private const string CapabilitiesPath = @"Software\OpenChoose\Capabilities";
        private const string ContextMenuKeyName = "OpenChoose";

        /// <summary>本程序完整路径。</summary>
        public static string ExecutablePath
        {
            get { return StartupManager.ExecutablePath; }
        }

        /// <summary>本程序文件名，例如 PPTChooser.exe。</summary>
        public static string ExeFileName
        {
            get { return Path.GetFileName(ExecutablePath); }
        }

        /// <summary>shell\open\command 的值，例如 "C:\...\PPTChooser.exe" "%1"。</summary>
        public static string OpenCommand
        {
            get { return Launcher.Quote(ExecutablePath) + " \"%1\""; }
        }

        /// <summary>把本程序注册为 PPT 文件的可用打开方式。</summary>
        public static void Register()
        {
            string exe = ExecutablePath;
            using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
            {
                if (classes == null)
                {
                    throw new InvalidOperationException("无法写入 HKCU\\Software\\Classes。");
                }

                using (RegistryKey progId = classes.CreateSubKey(Program.ProgId))
                {
                    progId.SetValue(null, Program.FriendlyAppName, RegistryValueKind.String);
                    using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
                    {
                        icon.SetValue(null, exe + ",0", RegistryValueKind.String);
                    }

                    using (RegistryKey command = progId.CreateSubKey(@"shell\open\command"))
                    {
                        command.SetValue(null, OpenCommand, RegistryValueKind.String);
                    }
                }

                using (RegistryKey application = classes.CreateSubKey(@"Applications\" + ExeFileName))
                {
                    application.SetValue("FriendlyAppName", Program.FriendlyAppName, RegistryValueKind.String);
                    using (RegistryKey command = application.CreateSubKey(@"shell\open\command"))
                    {
                        command.SetValue(null, OpenCommand, RegistryValueKind.String);
                    }

                    using (RegistryKey icon = application.CreateSubKey("DefaultIcon"))
                    {
                        icon.SetValue(null, exe + ",0", RegistryValueKind.String);
                    }

                    using (RegistryKey supported = application.CreateSubKey("SupportedTypes"))
                    {
                        foreach (string ext in Program.SupportedExtensions)
                        {
                            supported.SetValue(ext, string.Empty, RegistryValueKind.String);
                        }
                    }
                }

                foreach (string ext in Program.SupportedExtensions)
                {
                    using (RegistryKey openWithProgids = classes.CreateSubKey(ext + @"\OpenWithProgids"))
                    {
                        openWithProgids.SetValue(Program.ProgId, string.Empty, RegistryValueKind.String);
                    }

                    using (RegistryKey openWithList = classes.CreateSubKey(ext + @"\OpenWithList"))
                    {
                        openWithList.SetValue(ExeFileName, string.Empty, RegistryValueKind.String);
                    }

                    // 右键菜单：用 OpenChoose 打开
                    using (RegistryKey verb = classes.CreateSubKey(ext + @"\shell\" + ContextMenuKeyName))
                    {
                        verb.SetValue(null, "用 OpenChoose 选择打开方式", RegistryValueKind.String);
                        verb.SetValue("Icon", exe + ",0", RegistryValueKind.String);
                        using (RegistryKey command = verb.CreateSubKey("command"))
                        {
                            command.SetValue(null, OpenCommand, RegistryValueKind.String);
                        }
                    }
                }
            }

            // 资源管理器的“打开方式”历史记录
            foreach (string ext in Program.SupportedExtensions)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(FileExtsPath + ext + @"\OpenWithProgids"))
                {
                    if (key != null)
                    {
                        key.SetValue(Program.ProgId, string.Empty, RegistryValueKind.String);
                    }
                }
            }

            // 应用功能（Windows 8+ 的“默认应用”页面）
            using (RegistryKey capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                capabilities.SetValue("ApplicationName", Program.AppName, RegistryValueKind.String);
                capabilities.SetValue("ApplicationDescription", Program.AppDescription, RegistryValueKind.String);
                using (RegistryKey fileAssociations = capabilities.CreateSubKey("FileAssociations"))
                {
                    foreach (string ext in Program.SupportedExtensions)
                    {
                        fileAssociations.SetValue(ext, Program.ProgId, RegistryValueKind.String);
                    }
                }
            }

            using (RegistryKey registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                if (registered != null)
                {
                    registered.SetValue(Program.AppName, CapabilitiesPath, RegistryValueKind.String);
                }
            }

            Log.Write("文件关联已注册: " + exe);
        }

        /// <summary>
        /// 确保本程序出现在右键“打开方式”列表里（托盘启动时静默补齐）。
        /// 失败只记日志、不打扰用户；已注册时不做任何写入。
        /// </summary>
        public static bool EnsureRegistered()
        {
            try
            {
                if (IsRegistered())
                {
                    return true;
                }

                Register();
                Log.Write("启动时补齐文件关联注册（打开方式列表）");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("启动时补齐文件关联注册失败", ex);
                return false;
            }
        }

        /// <summary>把本程序设为 .ppt/.pptx 等的默认打开方式。</summary>
        public static SetDefaultResult SetAsDefault()
        {
            SetDefaultResult result = new SetDefaultResult();
            Register();
            result.Registered = true;

            using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
            {
                foreach (string ext in Program.SupportedExtensions)
                {
                    if (HasUserChoice(ext))
                    {
                        // Windows 10/11 的 UserChoice 带哈希校验，第三方程序无法直接修改
                        result.ProtectedExtensions.Add(ext);
                        Log.Write(ext + " 的默认应用由 Windows 保护（UserChoice），需要用户在系统设置中选择。");
                        continue;
                    }

                    // 注意：要写的是 HKCU\Software\Classes\.pptx 这个子键的“默认值”
                    using (RegistryKey extensionKey = classes.CreateSubKey(ext))
                    {
                        if (extensionKey != null)
                        {
                            extensionKey.SetValue(null, Program.ProgId, RegistryValueKind.String);
                            result.LegacyWritten = true;
                        }
                    }
                }
            }

            if (result.LegacyWritten)
            {
                NotifyShellOfAssociationChange();
            }

            return result;
        }

        /// <summary>取消文件关联并清理本程序写入的注册表项。</summary>
        public static void Unregister()
        {
            using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(ClassesPath, true))
            {
                if (classes != null)
                {
                    DeleteSubKeyTree(classes, Program.ProgId);
                    DeleteSubKeyTree(classes, @"Applications\" + ExeFileName);

                    foreach (string ext in Program.SupportedExtensions)
                    {
                        DeleteValue(classes, ext + @"\OpenWithProgids", Program.ProgId);
                        DeleteValue(classes, ext + @"\OpenWithList", ExeFileName);
                        DeleteSubKeyTree(classes, ext + @"\shell\" + ContextMenuKeyName);

                        using (RegistryKey extKey = classes.OpenSubKey(ext, true))
                        {
                            if (extKey != null)
                            {
                                string current = extKey.GetValue(null) as string;
                                if (string.Equals(current, Program.ProgId, StringComparison.OrdinalIgnoreCase))
                                {
                                    extKey.DeleteValue(null, false);
                                }
                            }
                        }
                    }
                }
            }

            foreach (string ext in Program.SupportedExtensions)
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(FileExtsPath + ext + @"\OpenWithProgids", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(Program.ProgId, false);
                    }
                }
            }

            using (RegistryKey registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            {
                if (registered != null)
                {
                    registered.DeleteValue(Program.AppName, false);
                }
            }

            DeleteSubKeyTree(Registry.CurrentUser, @"Software\OpenChoose");
            Log.Write("文件关联已取消");
        }

        // ---------- 查询 ----------

        /// <summary>系统是否为该扩展名写入了受保护的默认应用（UserChoice）。</summary>
        public static bool HasUserChoice(string extension)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(FileExtsPath + extension + @"\UserChoice", false))
                {
                    return key != null && key.GetValue("ProgId") != null;
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取 UserChoice 失败: " + extension, ex);
                return false;
            }
        }

        /// <summary>读取系统为扩展名记录的默认 ProgID（UserChoice）。</summary>
        public static string GetUserChoiceProgId(string extension)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(FileExtsPath + extension + @"\UserChoice", false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    return key.GetValue("ProgId") as string;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>该扩展名当前生效的 ProgID。</summary>
        public static string GetEffectiveProgId(string extension)
        {
            string progId = GetUserChoiceProgId(extension);
            if (!string.IsNullOrEmpty(progId))
            {
                return progId;
            }

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(extension, false))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取扩展名关联失败: " + extension, ex);
            }

            return null;
        }

        /// <summary>该扩展名当前生效的打开命令。</summary>
        public static string GetEffectiveOpenCommand(string extension)
        {
            string progId = GetEffectiveProgId(extension);
            if (string.IsNullOrEmpty(progId))
            {
                return null;
            }

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command", false))
                {
                    if (key != null)
                    {
                        return key.GetValue(null) as string;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取打开命令失败: " + progId, ex);
            }

            return null;
        }

        /// <summary>本程序是否已注册为 PPT 的打开方式。</summary>
        public static bool IsRegistered()
        {
            try
            {
                using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(ClassesPath, false))
                {
                    if (classes == null)
                    {
                        return false;
                    }

                    using (RegistryKey progId = classes.OpenSubKey(Program.ProgId, false))
                    {
                        return progId != null && progId.OpenSubKey(@"shell\open\command") != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // ---------- 内部辅助方法 ----------

        private static void DeleteSubKeyTree(RegistryKey parent, string path)
        {
            try
            {
                if (parent != null)
                {
                    parent.DeleteSubKeyTree(path, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("删除注册表项失败: " + path, ex);
            }
        }

        private static void DeleteValue(RegistryKey parent, string subKeyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = parent == null ? null : parent.OpenSubKey(subKeyPath, true))
                {
                    if (key != null && key.GetValue(valueName) != null)
                    {
                        key.DeleteValue(valueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("删除注册表值失败: " + subKeyPath + "\\" + valueName, ex);
            }
        }

        /// <summary>通知资源管理器刷新关联（图标/打开方式）。</summary>
        private static void NotifyShellOfAssociationChange()
        {
            try
            {
                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Write("通知资源管理器失败", ex);
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
