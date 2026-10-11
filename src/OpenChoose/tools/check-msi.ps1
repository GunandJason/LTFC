# check-msi.ps1
# 验证 OpenChoose.msi：静默安装 -> 检查文件/注册表/快捷方式 -> 静默卸载 -> 检查清理
# 用法： powershell -ExecutionPolicy Bypass -File tools\check-msi.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$msi = Join-Path $root 'dist\OpenChoose.msi'
$out = Join-Path $root 'dist\msi-check.txt'
$lines = New-Object System.Collections.Generic.List[string]
$script:failures = 0

function Add-Line([string]$text) { $script:lines.Add($text) }
function Check([string]$name, [bool]$ok, [string]$detail) {
    $state = 'PASS'
    if (-not $ok) { $state = 'FAIL'; $script:failures++ }
    $line = "[$state] $name -> $detail"
    $script:lines.Add($line)
    Write-Host $line
}

function ReadDefault([string]$subKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return $null }
    $value = $key.GetValue('')
    $key.Close()
    if ($null -eq $value) { return $null }
    return [string]$value
}

function ReadValue([string]$subKey, [string]$name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return $null }
    $value = $key.GetValue($name)
    $key.Close()
    if ($null -eq $value) { return $null }
    return [string]$value
}

function RunMsi([string]$arguments, [string]$logFile) {
    $process = Start-Process msiexec.exe -ArgumentList $arguments -Wait -PassThru
    if ($logFile) { Add-Line ('msiexec 日志: ' + $logFile) }
    return $process.ExitCode
}

if (-not (Test-Path $msi)) { throw "找不到 $msi，请先构建" }

function FindArpEntry {
    $paths = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )

    $items = @()
    foreach ($path in $paths) {
        if (Test-Path $path) {
            $items += @(Get-ChildItem $path -ErrorAction SilentlyContinue | Where-Object {
                (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DisplayName -eq 'OpenChoose'
            })
        }
    }

    return $items
}

$installDir = Join-Path $env:LOCALAPPDATA 'OpenChoose'
$exe = Join-Path $installDir 'PPTChooser.exe'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OpenChoose'

Add-Line ('=== OpenChoose MSI 检查 ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ===')
Add-Line ('安装包: ' + $msi)

# ---------- 安装 ----------
$code = RunMsi @('/i', ('"' + $msi + '"'), '/qn', '/norestart', '/l*v', ('"' + (Join-Path $root 'dist\msi-install.log') + '"')) (Join-Path $root 'dist\msi-install.log')
Check '1) 静默安装' ($code -eq 0) ("exit=" + $code)

Check '2) 程序文件已安装' (Test-Path $exe) $exe

$command = ReadDefault 'Software\Classes\OpenChoose.PPT\shell\open\command'
Check '3) ProgID 打开命令' ($command -like ('*' + $exe + '*" "%1"')) $command

$pptx = ReadDefault 'Software\Classes\.pptx'
Check '4) .pptx 默认指向本程序' ($pptx -eq 'OpenChoose.PPT') ('.pptx=' + $pptx)

$openWith = ReadValue 'Software\Classes\.pptx\OpenWithProgids' 'OpenChoose.PPT'
$supported = ReadValue 'Software\Classes\Applications\PPTChooser.exe\SupportedTypes' '.pptx'
$registered = ReadValue 'Software\RegisteredApplications' 'OpenChoose'
Check '5) 加入“打开方式”列表' ($null -ne $openWith -and $null -ne $supported -and $registered -eq 'Software\OpenChoose\Capabilities') `
    ('OpenWithProgids=' + ($null -ne $openWith) + '; SupportedTypes=' + ($null -ne $supported) + '; RegisteredApplications=' + $registered)

$runValue = ReadValue 'Software\Microsoft\Windows\CurrentVersion\Run' 'OpenChoose'
Check '6) 开机自启动项' ($runValue -like ('*' + $exe + '*')) $runValue

$shortcuts = @()
if (Test-Path $startMenu) { $shortcuts = @(Get-ChildItem $startMenu -Filter '*.lnk') }
Check '7) 开始菜单快捷方式' ($shortcuts.Count -ge 3) (($shortcuts | Select-Object -ExpandProperty Name) -join ' / ')

$report = Join-Path $root 'dist\msi-selftest.txt'
Start-Process -FilePath $exe -ArgumentList @('--selftest', ('"' + $report + '"'), '--silent') -Wait | Out-Null
Start-Sleep -Seconds 1
$reportText = ''
if (Test-Path $report) { $reportText = (Get-Content $report -Raw) }
Check '8) 安装后的程序可运行（诊断报告）' `
    ($reportText.Contains('.NETFramework,Version=v4.5') -and $reportText.Contains('本程序已注册为 PPT 打开方式: 是') -and $reportText.Contains('状态: 已启用')) `
    ('报告: ' + $report)

$arp = @(FindArpEntry)
$arpName = ''
if ($arp.Count -gt 0) { $arpName = (Get-ItemProperty $arp[0].PSPath).DisplayVersion }
Check '9) 控制面板卸载项' ($arp.Count -ge 1) ('版本=' + $arpName)

# ---------- 卸载 ----------
$code = RunMsi @('/x', ('"' + $msi + '"'), '/qn', '/norestart', '/l*v', ('"' + (Join-Path $root 'dist\msi-uninstall.log') + '"')) (Join-Path $root 'dist\msi-uninstall.log')
Check '10) 静默卸载' ($code -eq 0) ("exit=" + $code)

Check '11) 程序文件已删除' (-not (Test-Path $exe)) $exe
Check '12) ProgID 已清理' ($null -eq (ReadDefault 'Software\Classes\OpenChoose.PPT')) '(无)'
Check '13) .pptx 默认值已恢复' ((ReadDefault 'Software\Classes\.pptx') -ne 'OpenChoose.PPT') ('.pptx=' + (ReadDefault 'Software\Classes\.pptx'))
Check '14) 自启动项已清理' ($null -eq (ReadValue 'Software\Microsoft\Windows\CurrentVersion\Run' 'OpenChoose')) '(无)'
Check '15) 开始菜单已清理' (-not (Test-Path $startMenu)) $startMenu
Check '16) 控制面板卸载项已清理' ((@(FindArpEntry)).Count -eq 0) ('条目数=' + (@(FindArpEntry)).Count)
Check '17) 安装目录已清理' (-not (Test-Path $installDir)) $installDir

if ($script:failures -eq 0) {
    $lines.Add('=== 结果: 全部通过 ===')
} else {
    $lines.Add('=== 结果: ' + $script:failures + ' 项失败 ===')
}

$lines | Set-Content -Path $out -Encoding UTF8
Write-Host ('日志: ' + $out)
if ($script:failures -eq 0) { exit 0 } else { exit 1 }
