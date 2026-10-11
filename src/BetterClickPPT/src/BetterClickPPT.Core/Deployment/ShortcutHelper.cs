using System.Runtime.InteropServices;
using System.Text;

namespace BetterClickPPT.Core.Deployment;

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkComObject
{
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);

    void GetIDList(out IntPtr ppidl);

    void SetIDList(IntPtr pidl);

    void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out short pwHotkey);

    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

    void Resolve(IntPtr hwnd, int fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010b-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

/// <summary>创建 / 删除 Windows 快捷方式（.lnk）。</summary>
public static class ShortcutHelper
{
    /// <summary>创建快捷方式。</summary>
    public static void Create(string linkPath, string targetPath, string arguments, string workingDirectory, string description)
    {
        AppPaths.EnsureDirectory(Path.GetDirectoryName(linkPath)!);

        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLinkComObject();
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);

            if (File.Exists(targetPath))
            {
                link.SetIconLocation(targetPath, 0);
            }

            ((IPersistFile)link).Save(linkPath, false);
        }
        finally
        {
            if (link is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(link);
                }
                catch
                {
                    // 忽略释放失败。
                }
            }
        }
    }

    /// <summary>删除快捷方式。</summary>
    public static void Delete(string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
        }
        catch
        {
            // 忽略删除失败。
        }
    }

    /// <summary>开始菜单快捷方式路径（名称用显示名，用户一眼能认出来）。</summary>
    public static string GetStartMenuLinkPath() => Path.Combine(AppPaths.StartMenuDirectory, AppPaths.ProductDisplayName + ".lnk");

    /// <summary>桌面快捷方式路径。</summary>
    public static string GetDesktopLinkPath() => Path.Combine(AppPaths.DesktopDirectory, AppPaths.ProductDisplayName + ".lnk");

    /// <summary>
    /// 清理改名（BetterClickPPT 触控点击翻页增强器 → PPT 触摸增强工具）留下的旧快捷方式，
    /// 避免升级后桌面上 / 开始菜单里同时出现两个图标。
    /// </summary>
    public static void CleanupLegacyLinks()
    {
        Delete(Path.Combine(AppPaths.DesktopDirectory, AppPaths.LegacyProductDisplayName + ".lnk"));
        Delete(Path.Combine(AppPaths.LegacyStartMenuDirectory, AppPaths.ProductName + ".lnk"));
        Delete(Path.Combine(AppPaths.LegacyStartMenuDirectory, AppPaths.LegacyProductDisplayName + ".lnk"));
        TryDeleteEmptyDirectory(AppPaths.LegacyStartMenuDirectory);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // 忽略删除失败。
        }
    }
}
