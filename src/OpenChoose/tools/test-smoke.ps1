# test-smoke.ps1
# OpenChoose / PPTChooser 冒烟测试：
#   1) --register    注册文件关联
#   2) --setdefault  设为默认打开方式
#   3) --startup     开机自启动（托盘）
#   4) 双击文件场景  PPTChooser.exe "文件.pptx" 弹出选择窗口
#   5) 记住选择场景  直接以 xxx.exe "文件.pptx" 启动被选程序（含空格/中文路径）
#   6) --unregister  清理
#
# 用法： powershell -ExecutionPolicy Bypass -File tools\test-smoke.ps1

param(
    [string]$Exe = '',
    [string]$Work = '',
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'src\PPTChooser\bin\Release\PPTChooser.exe' }
if (-not $Work) { $Work = Join-Path $root 'dist' }
$logPath = Join-Path $Work 'smoke-test.log'
$script:failures = 0
$script:lines = New-Object System.Collections.Generic.List[string]

function Report([string]$name, [bool]$ok, [string]$detail) {
    $state = 'PASS'
    if (-not $ok) { $state = 'FAIL'; $script:failures++ }
    $line = "[$state] $name"
    if ($detail) { $line = $line + ' -> ' + $detail }
    $script:lines.Add($line)
    Write-Host $line
}

function RunApp([string[]]$arguments, [switch]$NoWait) {
    # Start-Process 不会自动加引号，含空格的参数（文件路径）必须自己加
    $escaped = @()
    foreach ($argument in $arguments) {
        if ($argument -match '\s') { $escaped += ('"' + $argument + '"') } else { $escaped += $argument }
    }

    if ($NoWait) {
        return Start-Process -FilePath $Exe -ArgumentList $escaped -PassThru
    }

    $process = Start-Process -FilePath $Exe -ArgumentList $escaped -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # 超时说明窗口没有自动关闭，视为失败并结束该进程，避免脚本挂起
        try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
        return -9999
    }

    return $process.ExitCode
}

function ReadDefaultValue([string]$subKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return $null }
    $value = $key.GetValue('')
    $key.Close()
    if ($null -eq $value) { return $null }
    return [string]$value
}

function RegValue([string]$path, [string]$name) {
    try {
        $item = Get-ItemProperty -Path $path -ErrorAction Stop
        if ($null -eq $name) { return $item.'(default)' }
        return $item.$name
    } catch {
        return $null
    }
}

function ValueExists([string]$subKey, [string]$name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return $false }
    $names = $key.GetValueNames()
    $key.Close()
    return ($names -contains $name)
}

function GetProcessWindows([int]$processId) {
    $target = Join-Path $Work 'windows.txt'
    & $winList $processId $target | Out-Null
    if (Test-Path $target) { return (Get-Content $target -Raw) }
    return ''
}

# 从 WinList 输出里取出可见窗口的客户区尺寸
function GetClientSize([string]$windowsText) {
    $match = [regex]::Match($windowsText, 'visible=True.*?client=(\d+)x(\d+)')
    if (-not $match.Success) { return $null }
    return @{ Width = [int]$match.Groups[1].Value; Height = [int]$match.Groups[2].Value }
}

if (-not (Test-Path $Exe)) { throw "找不到 $Exe，请先运行 build.ps1" }
if (-not (Test-Path $Work)) { New-Item -ItemType Directory -Path $Work -Force | Out-Null }

$script:lines.Add('=== OpenChoose 冒烟测试 ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ===')
$script:lines.Add('程序: ' + $Exe)

# 准备测试文件（带空格与中文的路径）
$tempFolder = Join-Path $Work 'test-smoke'
$testFile = Join-Path $tempFolder 'my demo 示例.pptx'
New-Item -ItemType Directory -Path $tempFolder -Force | Out-Null
Set-Content -Path $testFile -Value 'OpenChoose smoke test' -Encoding UTF8

# 准备参数回显程序与窗口枚举工具（都带 DPI 感知清单，保证读到真实像素尺寸）
$argEcho = Join-Path $Work 'ArgEcho.exe'
$winList = Join-Path $Work 'WinList.exe'
$shootWindow = Join-Path $Work 'ShootWindow.exe'
$manifest = Join-Path $PSScriptRoot 'dpi-aware.manifest'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $argEcho)) {
    & $csc /nologo /target:exe ("/win32manifest:" + $manifest) ("/out:" + $argEcho) (Join-Path $PSScriptRoot 'ArgEcho.cs') | Out-Null
}
if (-not (Test-Path $winList)) {
    & $csc /nologo /target:exe ("/win32manifest:" + $manifest) ("/out:" + $winList) (Join-Path $PSScriptRoot 'WinList.cs') | Out-Null
}
if (-not (Test-Path $shootWindow)) {
    & $csc /nologo /target:exe /reference:System.Drawing.dll ("/win32manifest:" + $manifest) ("/out:" + $shootWindow) (Join-Path $PSScriptRoot 'ShootWindow.cs') | Out-Null
}
Report '准备测试环境' ((Test-Path $argEcho) -and (Test-Path $winList) -and (Test-Path $shootWindow)) ($argEcho + ' / ' + $winList)

# ---------- 0. 程序图标（鼠标指针）可加载 ----------
Add-Type -AssemblyName System.Drawing
$iconProbe = ''
try {
    $appIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe)
    if ($null -ne $appIcon) {
        $iconProbe = $appIcon.Width.ToString() + 'x' + $appIcon.Height.ToString()
        $appIcon.Dispose()
    }
} catch {
    $iconProbe = 'ERROR: ' + $_.Exception.Message
}
Report '0) 程序图标可加载（鼠标指针图标）' ($iconProbe -match '^\d+x\d+$') $iconProbe

# ---------- 1. 注册文件关联 ----------
$code = RunApp @('--register', '--silent')
$progIdCommand = ReadDefaultValue 'Software\Classes\OpenChoose.PPT\shell\open\command'
$openWith = ValueExists 'Software\Classes\.pptx\OpenWithProgids' 'OpenChoose.PPT'
$openWithList = ValueExists 'Software\Classes\.pptx\OpenWithList' 'PPTChooser.exe'
$applications = Test-Path 'HKCU:\Software\Classes\Applications\PPTChooser.exe\SupportedTypes'
$registered = RegValue 'HKCU:\Software\RegisteredApplications' 'OpenChoose'
Report '1) 注册文件关联 (--register)' `
    ($code -eq 0 -and $progIdCommand -like '*PPTChooser.exe*' -and $openWith -and $openWithList -and $applications -and $registered) `
    ("exit=$code; OpenWithProgids=$openWith; OpenWithList=$openWithList; RegisteredApplications=$registered")

# ---------- 2. 设为默认打开方式 ----------
$userChoice = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pptx\UserChoice' 'ProgId'
$code = RunApp @('--setdefault', '--silent')
$pptxDefault = ReadDefaultValue 'Software\Classes\.pptx'
$step2Ok = $code -eq 0 -and (($null -ne $userChoice -and $pptxDefault -ne 'OpenChoose.PPT') -or ($null -eq $userChoice -and $pptxDefault -eq 'OpenChoose.PPT'))
Report '2) 设为默认打开方式 (--setdefault)（尊重系统 UserChoice）' $step2Ok `
    ("exit=$code; .pptx 默认 ProgID=$pptxDefault; UserChoice=" + $userChoice)

# ---------- 3. 开机自启动 ----------
$code = RunApp @('--enable-autostart', '--silent')
$runValue = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'OpenChoose'
Report '3a) 写入开机自启动项 (--enable-autostart)' `
    ($code -eq 0 -and $runValue -like '*PPTChooser.exe*--startup*') `
    ("exit=$code; Run 值=" + $runValue)

RunApp @('--startup', '--silent') -NoWait | Out-Null
Start-Sleep -Seconds 3
$trayProcess = @(Get-Process -Name 'PPTChooser' -ErrorAction SilentlyContinue)
$trayWindows = ''
if ($trayProcess.Count -ge 1) { $trayWindows = GetProcessWindows $trayProcess[0].Id }
Report '3b) 开机自启动后托盘运行 (--startup)' ($trayProcess.Count -ge 1) `
    ("托盘进程数=" + $trayProcess.Count + "; 窗口=" + ($trayWindows -replace "`r`n", ' | '))
foreach ($p in $trayProcess) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# ---------- 4. 双击文件 -> 弹出选择窗口（100% 缩放基准） ----------
$chooser = RunApp @($testFile) -NoWait
Start-Sleep -Seconds 3
$chooser.Refresh()
$alive = -not $chooser.HasExited
$windows = ''
if ($alive) { $windows = GetProcessWindows $chooser.Id }
$titleOk = $windows -like '*选择打开 PPT 的程序*'
$client100 = GetClientSize $windows
if ($alive) {
    & $shootWindow $chooser.Id (Join-Path $Work 'shot-native.png') | Out-Null
}
Report '4) 弹出“选择打开 PPT 的程序”窗口' ($alive -and $titleOk) `
    ("进程存活=" + $alive + "; 客户区=" + $client100.Width + "x" + $client100.Height + "（截图 shot-native.png）")

# ---------- 4b. 进程 DPI 感知 = 系统级（高分屏不错位的前提） ----------
$awareness = -1
try {
    Add-Type -Namespace Win32 -Name DpiAware -MemberDefinition @'
[DllImport("shcore.dll")] public static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);
'@
    [Win32.DpiAware]::GetProcessDpiAwareness($chooser.Handle, [ref]$awareness) | Out-Null
} catch {
    $awareness = -2
}
Report '4b) 进程 DPI 感知（0=不感知 1=系统 2=每显示器）' ($awareness -eq 1) ("awareness=" + $awareness)
if ($alive) { Stop-Process -Id $chooser.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# ---------- 4c. 高 DPI 布局缩放（--scale） ----------
$ratio15 = 0
$ratio20 = 0
foreach ($scale in @(1.5, 2.0)) {
    $scaled = RunApp @($testFile, ('--scale=' + $scale)) -NoWait
    Start-Sleep -Seconds 3
    $scaled.Refresh()
    $scaledWindows = ''
    if (-not $scaled.HasExited) { $scaledWindows = GetProcessWindows $scaled.Id }
    $client = GetClientSize $scaledWindows
    if ($null -ne $client -and $client100.Width -gt 0) {
        if ($scale -eq 1.5) {
            $ratio15 = [Math]::Round($client.Width / $client100.Width, 3)
            & $shootWindow $scaled.Id (Join-Path $Work 'shot-scale-150.png') | Out-Null
        }
        if ($scale -eq 2.0) { $ratio20 = [Math]::Round($client.Width / $client100.Width, 3) }
    }
    if (-not $scaled.HasExited) { Stop-Process -Id $scaled.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1
}
Report '4c) 高 DPI 布局缩放（--scale=1.5 / 2.0 客户区按比例放大）' `
    (($ratio15 -ge 1.44 -and $ratio15 -le 1.56) -and ($ratio20 -ge 1.92 -and $ratio20 -le 2.08)) `
    ("150% 实测比例=" + $ratio15 + "; 200% 实测比例=" + $ratio20)

# ---------- 5. 记住选择 -> 直接以 xxx.exe "文件.pptx" 启动 ----------
$echoResult = Join-Path $env:TEMP 'openchoose-argecho.txt'
Remove-Item $echoResult -Force -ErrorAction SilentlyContinue
$configDir = Join-Path $env:APPDATA 'OpenChoose'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$configFile = Join-Path $configDir 'settings.ini'
Set-Content -Path $configFile -Encoding UTF8 -Value @(
    '; OpenChoose / PPTChooser 配置文件',
    'RememberChoice=1',
    ('Open.pptx=' + $argEcho),
    'Name.pptx=ArgEcho'
)
$code = RunApp @($testFile)
Start-Sleep -Seconds 3
$echoText = ''
if (Test-Path $echoResult) { $echoText = (Get-Content $echoResult -Raw).Trim() }
$launchLogged = ''
$appLog = Join-Path $configDir 'log.txt'
if (Test-Path $appLog) { $launchLogged = (Get-Content $appLog -Raw) }

# 注意：用 Contains 做字面比较，-like 会把 [] 当成通配符字符集
$launchedOk = $code -eq 0 -and
    $echoText.Contains('arg[0]=' + $testFile) -and
    $echoText.Contains('exists=True') -and
    $launchLogged.Contains('ArgEcho.exe')
Report '5) 直接启动 xxx.exe "文件.pptx"（含空格/中文路径）' $launchedOk `
    ("exit=" + $code + '; ' + ($echoText -replace "`r`n", ' | '))

# ---------- 6. 清理 ----------
Remove-Item $configFile -Force -ErrorAction SilentlyContinue
$code = RunApp @('--unregister', '--silent')
$autostartCode = RunApp @('--disable-autostart', '--silent')
$runAfter = RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'OpenChoose'
$cleaned = -not (Test-Path 'HKCU:\Software\Classes\OpenChoose.PPT')
$pptxAfter = ReadDefaultValue 'Software\Classes\.pptx'
Report '6) 取消关联并关闭自启动 (--unregister / --disable-autostart)' `
    ($code -eq 0 -and $autostartCode -eq 0 -and $cleaned -and $null -eq $runAfter -and $pptxAfter -ne 'OpenChoose.PPT') `
    ("exit=$code/$autostartCode; .pptx 默认 ProgID=" + $pptxAfter + "; Run 值=" + $runAfter)

if ($script:failures -eq 0) {
    $script:lines.Add('=== 结果: 全部通过 ===')
} else {
    $script:lines.Add('=== 结果: ' + $script:failures + ' 项失败 ===')
}

$script:lines | Set-Content -Path $logPath -Encoding UTF8
Write-Host ''
Write-Host ('日志: ' + $logPath)
if ($script:failures -eq 0) { exit 0 } else { exit 1 }

