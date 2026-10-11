# install.ps1 —— 不依赖 MSI 的备用安装/卸载脚本（同样的效果，完全不需要管理员权限）
#
#   安装： powershell -ExecutionPolicy Bypass -File install.ps1
#   卸载： powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
#
# 做四件事（与 OpenChoose.msi 一致）：
#   1) 把 PPTChooser.exe 复制到 %LOCALAPPDATA%\OpenChoose
#   2) PPTChooser.exe --register          注册文件关联（HKCU）
#   3) PPTChooser.exe --enable-autostart  写入开机自启动（HKCU Run）
#   4) 创建开始菜单快捷方式，并写入“应用和功能”里的卸载入口（HKCU）
param(
    [switch]$Uninstall,
    [string]$Exe,
    [switch]$NoAutostart,
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'
$appName = 'OpenChoose'
$displayName = 'OpenChoose（PPT 打开方式选择器）'
$installDir = Join-Path $env:LOCALAPPDATA 'OpenChoose'
$targetExe = Join-Path $installDir 'PPTChooser.exe'
$arpKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenChoose'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

function Write-Step([string]$text) {
    Write-Host ('  ' + $text)
}

function Remove-StartMenuShortcuts {
    $names = @('OpenChoose（选择打开 PPT 的程序）.lnk', '设为默认打开方式.lnk', '诊断报告.lnk')
    $removed = 0
    foreach ($name in $names) {
        foreach ($dir in @($startMenuDir, (Join-Path $startMenuDir $appName))) {
            $path = Join-Path $dir $name
            if (Test-Path $path) {
                Remove-Item $path -Force -ErrorAction SilentlyContinue
                $removed++
            }
        }
    }
    $sub = Join-Path $startMenuDir $appName
    if ((Test-Path $sub) -and -not (Get-ChildItem $sub -Force -ErrorAction SilentlyContinue)) {
        Remove-Item $sub -Force -ErrorAction SilentlyContinue
    }
    return $removed
}

if ($Uninstall) {
    Write-Host ("卸载 " + $displayName + " ...")
    if (Test-Path $targetExe) {
        Write-Step '取消文件关联 ...'
        & $targetExe --unregister | Out-Null
        Write-Step '关闭开机自启动 ...'
        & $targetExe --disable-autostart | Out-Null
    }
    Write-Step '删除开始菜单快捷方式 ...'
    $null = Remove-StartMenuShortcuts
    Write-Step '删除“应用和功能”条目 ...'
    Remove-Item $arpKey -Recurse -Force -ErrorAction SilentlyContinue

    Get-Process -Name 'PPTChooser' -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.CloseMainWindow() | Out-Null } catch { }
    }
    Start-Sleep -Milliseconds 500
    Get-Process -Name 'PPTChooser' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Step '删除程序文件 ...'
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue }

    Write-Host '卸载完成。个人设置（%APPDATA%\OpenChoose\settings.ini）已保留，可自行删除。'
    exit 0
}

# ---------------- 安装 ----------------
if (-not $Exe) {
    $candidates = @(
        (Join-Path $PSScriptRoot 'src\PPTChooser\bin\Release\PPTChooser.exe'),
        (Join-Path $PSScriptRoot 'dist\PPTChooser.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { $Exe = $candidate; break }
    }
}
if (-not $Exe -or -not (Test-Path $Exe)) {
    throw '找不到 PPTChooser.exe。请先运行 build.ps1 编译，或用 -Exe 指定 exe 路径。'
}
$Exe = (Resolve-Path $Exe).Path

Write-Host ("安装 " + $displayName + " ...")
Write-Step ('复制程序文件到 ' + $installDir)
if (-not (Test-Path $installDir)) { New-Item -ItemType Directory -Path $installDir -Force | Out-Null }
if ($Exe -ne $targetExe) {
    Get-Process -Name 'PPTChooser' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Copy-Item $Exe $targetExe -Force
}

Write-Step '注册文件关联（HKCU，无需管理员）...'
& $targetExe --register | Out-Null

if (-not $NoAutostart) {
    Write-Step '启用开机自启动 ...'
    & $targetExe --enable-autostart | Out-Null
}

if (-not $NoShortcuts) {
    Write-Step '创建开始菜单快捷方式 ...'
    $null = Remove-StartMenuShortcuts
    $shell = New-Object -ComObject WScript.Shell
    $shortcuts = @(
        @{ Name = 'OpenChoose（选择打开 PPT 的程序）'; Arguments = ''; Description = '选择用哪个程序打开 PPT' },
        @{ Name = '设为默认打开方式'; Arguments = '--setdefault'; Description = '把 OpenChoose 设为 .ppt / .pptx 的默认打开方式' },
        @{ Name = '诊断报告'; Arguments = '--selftest'; Description = '检测本机的 PPT 程序与文件关联状态' }
    )
    foreach ($item in $shortcuts) {
        $link = $shell.CreateShortcut((Join-Path $startMenuDir ($item.Name + '.lnk')))
        $link.TargetPath = $targetExe
        $link.Arguments = $item.Arguments
        $link.WorkingDirectory = $installDir
        $link.Description = $item.Description
        $link.IconLocation = ($targetExe + ',0')
        $link.Save()
    }
}

Write-Step '写入“应用和功能”卸载入口 ...'
New-Item -Path $arpKey -Force | Out-Null
Set-ItemProperty -Path $arpKey -Name 'DisplayName' -Value $displayName
Set-ItemProperty -Path $arpKey -Name 'Publisher' -Value $appName
Set-ItemProperty -Path $arpKey -Name 'DisplayVersion' -Value '1.0.0.0'
Set-ItemProperty -Path $arpKey -Name 'Comments' -Value '双击 PPT 文件时选择用 PowerPoint、WPS 或其它程序打开'
Set-ItemProperty -Path $arpKey -Name 'InstallLocation' -Value $installDir
Set-ItemProperty -Path $arpKey -Name 'DisplayIcon' -Value ($targetExe + ',0')
Set-ItemProperty -Path $arpKey -Name 'EstimatedSize' -Value ([int]((Get-Item $targetExe).Length / 1024)) -Type DWord
Set-ItemProperty -Path $arpKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $arpKey -Name 'NoRepair' -Value 1 -Type DWord
$uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installDir 'install.ps1') + '" -Uninstall'
Set-ItemProperty -Path $arpKey -Name 'UninstallString' -Value $uninstallCommand
Set-ItemProperty -Path $arpKey -Name 'QuietUninstallString' -Value $uninstallCommand
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') (Join-Path $installDir 'install.ps1') -Force

Write-Host ''
Write-Host '安装完成！'
Write-Host ('  程序位置 : ' + $targetExe)
Write-Host '  使用方式 : 双击任意 .ppt / .pptx 文件即可弹出选择窗口'
Write-Host ('  卸载方式 : “设置 → 应用 → 已安装的应用”里找到 ' + $appName + ' 卸载，或运行 install.ps1 -Uninstall')
Write-Host ''
Write-Host '如果双击 PPT 仍然用原来的程序打开，请到“设置 → 应用 → 默认应用”把 .pptx / .ppt'
Write-Host '关联到 OpenChoose（Windows 10/11 会保护默认应用设置，需要手动确认一次）。'

