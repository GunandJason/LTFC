#requires -Version 5.1
<#
    安装向导（GUI）自动化验证脚本。
    使用 UI Automation 驱动真实窗口：欢迎页 → 选择目标软件（检查自动检测结果）→
    安装选项 → 开始安装 → 完成页，最后校验安装结果并清理。

    用法：
      powershell -ExecutionPolicy Bypass -File tools\verify-setup-wizard.ps1
      powershell -ExecutionPolicy Bypass -File tools\verify-setup-wizard.ps1 -AppPath dist\BetterClickPPT-Setup.exe
#>
param(
    [string]$AppPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $AppPath) {
    $AppPath = @(
        (Join-Path $root 'dist\BetterClickPPT-Setup.exe'),
        (Join-Path $root 'src\BetterClickPPT.App\bin\Release\net8.0-windows\BetterClickPPT.exe'),
        (Join-Path $root 'src\BetterClickPPT.App\bin\Debug\net8.0-windows\BetterClickPPT.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $AppPath -or -not (Test-Path $AppPath)) {
    throw '未找到安装程序，请先运行 build\build.ps1，或用 -AppPath 指定路径。'
}

$AppPath = (Resolve-Path $AppPath).Path
Write-Host "待验证安装程序：$AppPath"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$results = New-Object System.Collections.Generic.List[string]
function Assert([bool]$condition, [string]$message) {
    if ($condition) {
        Write-Host "  [通过] $message" -ForegroundColor Green
        $results.Add("PASS $message")
    }
    else {
        Write-Host "  [失败] $message" -ForegroundColor Red
        $results.Add("FAIL $message")
    }
}

function Get-WizardWindow($processId, [int]$timeoutSeconds = 20) {
    $rootElement = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)

    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $window = $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 300
    }

    return $null
}

function Find-ByName($window, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-Containing($window, [string]$text) {
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        $name = $element.Current.Name
        if ($name -and $name.Contains($text)) { return $element }
    }

    return $null
}

function Invoke-Element($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Get-CheckBox($window, [string]$namePart) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::CheckBox)
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($element in $all) {
        if ($element.Current.Name -and $element.Current.Name.Contains($namePart)) { return $element }
    }

    return $null
}

$process = $null
try {
    Write-Host ''
    Write-Host '--- 启动安装向导 ---'
    $process = Start-Process -FilePath $AppPath -PassThru
    $window = Get-WizardWindow -processId $process.Id
    Assert ($null -ne $window) '安装向导窗口已出现'
    if ($null -eq $window) { throw '未能找到安装向导窗口。' }

    Write-Host ''
    Write-Host '--- 欢迎页 ---'
    $startButton = Find-ByName $window '开始安装'
    Assert ($null -ne $startButton) '欢迎页存在「开始安装」按钮'
    Assert ($null -ne (Find-Containing $window '自动查找')) '欢迎页包含自动查找安装位置的说明'

    Invoke-Element $startButton
    Start-Sleep -Milliseconds 400

    Write-Host ''
    Write-Host '--- 选择目标软件页（自动检测）---'
    $powerPointCheck = Get-CheckBox $window 'PowerPoint'
    Assert ($null -ne $powerPointCheck) '存在 PowerPoint 勾选项'
    if ($null -ne $powerPointCheck) {
        $toggle = $powerPointCheck.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        Write-Host ("  PowerPoint 勾选状态：" + $toggle.Current.ToggleState)
        Assert ($toggle.Current.ToggleState.ToString() -eq 'On') '检测到 PowerPoint 后默认已勾选'
    }

    Assert ($null -ne (Find-Containing $window '已检测到')) '页面显示「已检测到」的自动检测结果'

    $nextButton = Find-ByName $window '下一步'
    Assert ($null -ne $nextButton) '选择目标软件页存在「下一步」按钮'
    Invoke-Element $nextButton
    Start-Sleep -Milliseconds 400

    Write-Host ''
    Write-Host '--- 安装选项页 ---'
    Assert ($null -ne (Find-Containing $window '安装位置')) '安装选项页显示安装位置'
    Assert ($null -ne (Get-CheckBox $window '开机自动启动')) '存在「开机自动启动」选项'

    $installButton = Find-ByName $window '开始安装'
    Assert ($null -ne $installButton) '安装选项页存在「开始安装」按钮'

    Write-Host ''
    Write-Host '--- 执行安装并等待完成页 ---'
    Invoke-Element $installButton
    Start-Sleep -Seconds 6

    Assert ($null -ne (Find-Containing $window '安装成功')) '完成页显示「安装成功」'
    Assert ($null -eq (Find-Containing $window '安装失败')) '没有出现「安装失败」'

    $installed = Join-Path $env:LOCALAPPDATA 'Programs\BetterClickPPT\BetterClickPPT.exe'
    Assert (Test-Path $installed) '安装目录内已存在主程序（由向导完成安装）'
}
catch {
    Write-Host ('  [异常] ' + $_.Exception.Message) -ForegroundColor Red
    $results.Add('FAIL 执行异常：' + $_.Exception.Message)
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 600

    Write-Host ''
    Write-Host '--- 清理（静默卸载）---'
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\BetterClickPPT\BetterClickPPT.exe'
    if (Test-Path $installed) {
        $uninstall = Start-Process -FilePath $installed -ArgumentList '--uninstall', '--quiet' -Wait -PassThru
        Write-Host ("  卸载退出码：" + $uninstall.ExitCode)
    }

    Start-Sleep -Milliseconds 800

    # 清理是“卸载进程退出后再删除目录”，这里最多等 20 秒
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and (Test-Path $installed)) {
        Start-Sleep -Milliseconds 500
    }

    Assert (-not (Test-Path $installed)) '测试后安装目录已被清理'

    Write-Host ''
    Write-Host '=================================================='
    Write-Host ' 验证结论'
    Write-Host '=================================================='
    $results | ForEach-Object { Write-Host ('  ' + $_) }

    $failureCount = @($results | Where-Object { $_ -like 'FAIL*' }).Count
    Write-Host ''
    if ($failureCount -eq 0) {
        Write-Host '安装向导验证通过。' -ForegroundColor Green
    }
    else {
        Write-Host "安装向导验证失败 $failureCount 项。" -ForegroundColor Red
    }

    exit $failureCount
}
