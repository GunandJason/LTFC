using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OpenChoose
{
    /// <summary>
    /// 在本机检测可用于打开 PPT 的程序（PowerPoint / WPS / LibreOffice）。
    /// </summary>
    internal static class ProgramFinder
    {
        /// <summary>返回列表项：PowerPoint、WPS Office、LibreOffice Impress、选择其他程序...</summary>
        public static List<ProgramCandidate> DetectAll()
        {
            List<ProgramCandidate> list = new List<ProgramCandidate>();
            list.Add(new ProgramCandidate("powerpoint", "PowerPoint", FindPowerPoint()));
            list.Add(new ProgramCandidate("wps", "WPS Office", FindWps()));
            list.Add(new ProgramCandidate("libreoffice", "LibreOffice Impress", FindLibreOffice()));

            ProgramCandidate other = new ProgramCandidate("other", "选择其他程序", null);
            other.IsOther = true;
            list.Add(other);

            return list;
        }

        /// <summary>查找 Microsoft PowerPoint。</summary>
        public static string FindPowerPoint()
        {
            string path = SearchAppPaths("POWERPNT.EXE");
            if (path != null)
            {
                return path;
            }

            string[] relativePaths =
            {
                @"Microsoft Office\root\Office16\POWERPNT.EXE",
                @"Microsoft Office\root\Office15\POWERPNT.EXE",
                @"Microsoft Office\root\Office14\POWERPNT.EXE",
                @"Microsoft Office\Office16\POWERPNT.EXE",
                @"Microsoft Office\Office15\POWERPNT.EXE",
                @"Microsoft Office\Office14\POWERPNT.EXE"
            };

            foreach (string root in OfficeRoots())
            {
                string found = FirstExisting(root, relativePaths);
                if (found != null)
                {
                    return found;
                }

                found = VersionedSearch(Path.Combine(root, "Microsoft Office", "root"), "Office*", "POWERPNT.EXE");
                if (found != null)
                {
                    return found;
                }

                found = VersionedSearch(root, "Microsoft Office*", @"Office16\POWERPNT.EXE");
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>查找 WPS Office（演示组件 wpp.exe）。</summary>
        public static string FindWps()
        {
            string[] appPathNames = { "wpp.exe", "wps.exe", "ksolaunch.exe" };
            foreach (string name in appPathNames)
            {
                string path = SearchAppPaths(name);
                if (path != null)
                {
                    return path;
                }
            }

            // 金山注册表中记录的安装目录
            foreach (string keyPath in new[]
            {
                @"Software\Kingsoft\Office\6.0\common",
                @"Software\Kingsoft\Office\6.0\common\InstallRoot"
            })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    string root = ReadRegistryString(hive, keyPath, "InstallRoot");
                    if (string.IsNullOrEmpty(root))
                    {
                        root = ReadRegistryString(hive, keyPath, null);
                    }

                    string found = FromWpsRoot(root);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            foreach (string root in OfficeRoots())
            {
                string found = VersionedSearch(root, "WPS Office*", @"office6\wpp.exe");
                if (found == null)
                {
                    found = VersionedSearch(root, @"Kingsoft\WPS Office*", @"office6\wpp.exe");
                }

                if (found != null)
                {
                    return found;
                }
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string wpsLocal = VersionedSearch(Path.Combine(localAppData, "Kingsoft"), "WPS Office*", @"office6\wpp.exe");
            if (wpsLocal != null)
            {
                return wpsLocal;
            }

            return null;
        }

        /// <summary>查找 LibreOffice Impress。</summary>
        public static string FindLibreOffice()
        {
            string path = SearchAppPaths("soffice.exe");
            if (path != null)
            {
                return path;
            }

            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                string installPath = ReadRegistryString(hive, @"SOFTWARE\LibreOffice\UNO\InstallPath", null);
                if (!string.IsNullOrEmpty(installPath))
                {
                    string candidate = Path.Combine(installPath, "soffice.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            foreach (string root in OfficeRoots())
            {
                string found = FirstExisting(root, @"LibreOffice\program\soffice.exe");
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // ---------- 内部辅助方法 ----------

        /// <summary>可能的安装根目录（64 位程序目录优先）。</summary>
        private static IEnumerable<string> OfficeRoots()
        {
            List<string> roots = new List<string>();
            string[] names = { "ProgramW6432", "ProgramFiles(x86)", "ProgramFiles" };
            foreach (string name in names)
            {
                string value = Environment.GetEnvironmentVariable(name);
                AddRoot(roots, value);
            }

            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            return roots;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            foreach (string existing in roots)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            roots.Add(path);
        }

        /// <summary>返回第一个存在的相对路径。</summary>
        private static string FirstExisting(string root, params string[] relativePaths)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            foreach (string relative in relativePaths)
            {
                string candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>在 baseDir 下按目录通配符查找程序，例如 ...\WPS Office\*\office6\wpp.exe。</summary>
        private static string VersionedSearch(string baseDir, string directoryPattern, string relativeExe)
        {
            try
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                {
                    return null;
                }

                string direct = Path.Combine(baseDir, relativeExe);
                if (File.Exists(direct))
                {
                    return direct;
                }

                foreach (string directory in Directory.GetDirectories(baseDir, directoryPattern))
                {
                    string candidate = Path.Combine(directory, relativeExe);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("扫描目录失败: " + baseDir, ex);
            }

            return null;
        }

        /// <summary>根据 WPS 安装根目录推断 wpp.exe。</summary>
        private static string FromWpsRoot(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            string[] candidates =
            {
                Path.Combine(root, @"office6\wpp.exe"),
                Path.Combine(root, "wpp.exe"),
                Path.Combine(root, "ksolaunch.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return VersionedSearch(root, "*", @"office6\wpp.exe");
        }

        /// <summary>通过 App Paths 注册表查找程序（类似 App Paths 的 where）。</summary>
        private static string SearchAppPaths(string executableName)
        {
            const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                string value = ReadRegistryString(hive, appPaths + executableName, null);
                if (!string.IsNullOrEmpty(value))
                {
                    value = value.Trim().Trim('"');
                    if (File.Exists(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <summary>读取注册表字符串（自动尝试 64 / 32 位视图）。</summary>
        private static string ReadRegistryString(RegistryHive hive, string subKeyPath, string valueName)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = baseKey.OpenSubKey(subKeyPath, false))
                    {
                        if (key != null)
                        {
                            string value = key.GetValue(valueName) as string;
                            if (!string.IsNullOrEmpty(value))
                            {
                                return value.Trim().Trim('"');
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("读取注册表失败: " + subKeyPath, ex);
                }
            }

            return null;
        }
    }
}
