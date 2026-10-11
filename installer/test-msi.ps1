# test-msi.ps1 —— 在“以管理员身份运行”的 PowerShell 里做一次完整的安装/卸载验证
#
# 它会：
#   1) 记录安装前本机已装的 LTFC / ClassTell 条目；
#   2) 静默安装 MSI（msiexec /i /qn，写详细日志）；
#   3) 核对安装目录结构、内置模块、以及旧版独立 ClassTell 是否已被卸载；
#   4) 卸载 MSI，核对安装目录与自启动项是否清理干净。
# 默认卸装后不留残留；想保留安装结果用 -KeepInstalled。
#
# 用法（必须管理员）：
#   powershell -ExecutionPolicy Bypass -File installer\test-msi.ps1
#   powershell -ExecutionPolicy Bypass -File installer\test-msi.ps1 -Msi release\LTFC-1.0.2_r.msi -KeepInstalled
#
# 注意：本脚本只做安装/卸载，不会运行 LTFC.exe（避免动到个人配置和自启动项）。
[CmdletBinding()]
param(
    [string]$Msi,
    [switch]$KeepInstalled,
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:TEMP ('ltfc-msi-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item $logDir -ItemType Directory -Force | Out-Null

function Step([string]$text) { Write-Host ''; Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan }
function Note([string]$text) { Write-Host ('    ' + $text) }
$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail) {
    $extra = ''
    if ($detail) { $extra = '  —— ' + $detail }
    if ($ok) { Write-Host ('    [通过] ' + $name + $extra) -ForegroundColor Green }
    else { Write-Host ('    [失败] ' + $name + $extra) -ForegroundColor Red }
    [void]$results.Add(@{ Name = $name; Ok = $ok; Detail = $detail })
}

# 安装列表（Uninstall 注册表是 MSI 产品的唯一权威来源）
function Get-InstalledProducts {
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $found = @()
    foreach ($p in $paths) {
        $found += Get-ItemProperty $p -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -and ($_.DisplayName -match 'LTFC|ClassTell|Lots_Tools') } |
            Select-Object DisplayName, DisplayVersion, PSChildName
    }
    return $found
}

Step '0/6 权限与输入检查'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw '请在“以管理员身份运行”的 PowerShell 里执行本脚本：安装 LTFC 是每机器安装，需要管理员权限（会写 C:\Program Files）。'
}
Note '管理员权限：OK'

if (-not $Msi) {
    $candidate = @(Get-ChildItem (Join-Path $root 'release') -Filter '*.msi' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
    if ($candidate.Count -eq 0) { throw ('没有找到 MSI，请先运行 installer\build-msi.ps1；或显式指定 -Msi。') }
    $Msi = $candidate[0].FullName
}
if (-not (Test-Path $Msi)) { throw ('找不到 MSI：' + $Msi) }
$Msi = (Resolve-Path $Msi).Path
Note ('安装包：' + $Msi + '  (' + [Math]::Round((Get-Item $Msi).Length / 1MB, 2) + ' MB)')

Step '1/6 安装前的状态'
$before = @(Get-InstalledProducts)
if ($before.Count -eq 0) { Note '（本机没有已安装的 LTFC / ClassTell 条目）' }
foreach ($p in $before) { Note ($p.DisplayName + '  ' + $p.DisplayVersion + '  [' + $p.PSChildName + ']') }
$oldClassTellBefore = @($before | Where-Object { $_.DisplayName -match 'ClassTell' })
$legacyDirBefore = Test-Path (Join-Path $env:ProgramFiles 'ClassTell')
Note ('旧 ClassTell 独立安装：' + $oldClassTellBefore.Count + ' 条；安装目录 ' + (Join-Path $env:ProgramFiles 'ClassTell') + ' 存在？' + $legacyDirBefore)

Step '2/6 静默安装'
$installLog = Join-Path $logDir 'install.log'
$p = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @(
    '/i', ('"' + $Msi + '"'), '/qn', '/norestart', '/l*v', ('"' + $installLog + '"'))
Note ('msiexec 退出码：' + $p.ExitCode)
Check '静默安装成功（退出码 0 或 3010）' ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) ('exit=' + $p.ExitCode + '，日志：' + $installLog)

# 安装目录：MSI 是 x64 包，装在 ProgramFiles64Folder。
# 32 位 PowerShell 里 $env:ProgramFiles 会指向 (x86)，所以优先用 ProgramW6432。
if (-not $InstallDir) {
    $programFiles64 = $env:ProgramW6432
    if (-not $programFiles64) { $programFiles64 = $env:ProgramFiles }
    $InstallDir = Join-Path $programFiles64 'LTFC'
}

Step '3/6 安装结果核对'
Note ('安装目录：' + $InstallDir)
$expected = @(
    'LTFC.exe',
    'LTFC.Core.dll',
    'LTFC.exe.config',
    'assets\LTFC.ico',
    'assets\赞赏码.jpg',
    'modules\ClassTell\ClassTell.exe',
    'modules\ClassTell\info.txt',
    'modules\OpenChoose\PPTChooser.exe',
    '许可（摘要）.txt'
)
foreach ($rel in $expected) {
    $full = Join-Path $InstallDir $rel
    Check ('已安装 ' + $rel) (Test-Path -LiteralPath $full) ''
}
Check '未把 command.txt 装进 Program Files（由程序写到 %APPDATA%）' `
      (-not (Test-Path -LiteralPath (Join-Path $InstallDir 'AutoNotice\command.txt'))) ''

$after = @(Get-InstalledProducts)
foreach ($item in $after) { Note ('已注册：' + $item.DisplayName + '  ' + $item.DisplayVersion + '  [' + $item.PSChildName + ']') }
$ltfc = @($after | Where-Object { $_.DisplayName -match 'LTFC|Lots_Tools' })
Check '控制面板出现 LTFC 产品条目' ($ltfc.Count -ge 1) (($ltfc | ForEach-Object { $_.DisplayName }) -join '、')

if ($oldClassTellBefore.Count -gt 0) {
    $oldLeft = @($after | Where-Object { $_.DisplayName -match 'ClassTell' })
    Check '旧版独立 ClassTell 已被卸载（LTFC 覆盖安装生效）' ($oldLeft.Count -eq 0) ('剩余 ' + $oldLeft.Count + ' 条')
    $legacyDirAfter = Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'ClassTell')
    Note ('旧 ClassTell 安装目录是否还在：' + $legacyDirAfter +
          '（目录里若有用户自建文件，MSI 卸载时不会删；以产品条目是否消失为准）')
} else {
    Note '安装前本机没有旧 ClassTell：本次只验证 LTFC 自身安装，接管旧包要在装过旧 ClassTell 的机器上验证'
}

Step '4/6 自启动项（HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LTFC）'
$runValue = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LTFC' -ErrorAction SilentlyContinue).LTFC
if ($runValue) { Note ('当前存在（LTFC 运行时自己写的）：' + $runValue) }
else { Note '当前没有（这个值由 LTFC 首次运行后写入，不是安装包写的）' }

if ($KeepInstalled) {
    Step '5/6 跳过卸载（-KeepInstalled）'
    Note ('安装结果保留在 ' + $InstallDir + '；测试结束后可手动用“添加或删除程序”卸载')
} else {
    Step '5/6 静默卸载'
    $uninstallLog = Join-Path $logDir 'uninstall.log'
    $p2 = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @(
        '/x', ('"' + $Msi + '"'), '/qn', '/norestart', '/l*v', ('"' + $uninstallLog + '"'))
    Note ('msiexec 退出码：' + $p2.ExitCode)
    Check '静默卸载成功（退出码 0 或 3010）' ($p2.ExitCode -eq 0 -or $p2.ExitCode -eq 3010) ('exit=' + $p2.ExitCode + '，日志：' + $uninstallLog)

    Step '6/6 卸载结果核对'
    Check '安装目录已删除' (-not (Test-Path -LiteralPath $InstallDir)) $InstallDir
    $left = @(Get-InstalledProducts | Where-Object { $_.DisplayName -match 'LTFC|Lots_Tools' })
    Check '控制面板条目已移除' ($left.Count -eq 0) ('剩余 ' + $left.Count + ' 条')
    $leftRun = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LTFC' -ErrorAction SilentlyContinue).LTFC
    Check 'HKCU 自启动项已清理（RemoveRegistry 生效）' (-not $leftRun) ('' + $leftRun)
    Note '个人数据保留（卸载不会删）：%APPDATA%\LTFC、%APPDATA%\ClassTell'
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ''
if ($failed.Count -eq 0) {
    Write-Host ('全部检查通过（' + $results.Count + ' 项）。') -ForegroundColor Green
} else {
    Write-Host ('有 ' + $failed.Count + ' / ' + $results.Count + ' 项未通过：') -ForegroundColor Red
    $failed | ForEach-Object { Write-Host ('  · ' + $_.Name + '  ' + $_.Detail) -ForegroundColor Red }
}
Write-Host ('日志目录：' + $logDir)
exit $failed.Count
