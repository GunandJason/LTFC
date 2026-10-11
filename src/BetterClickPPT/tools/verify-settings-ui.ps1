#requires -Version 5.1
<#
    设置窗口（GUI）自动化验证：启动托盘程序并打开设置窗口，
    用 UI Automation 检查三个标签页的关键控件（含“轻触的处理方式”）是否就绪，最后关闭进程。

    用法：
      powershell -ExecutionPolicy Bypass -File tools\verify-settings-ui.ps1
      powershell -ExecutionPolicy Bypass -File tools\verify-settings-ui.ps1 -AppPath dist\BetterClickPPT.exe
#>
param(
    [string]$AppPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $AppPath) {
    $AppPath = @(
        (Join-Path $root 'dist\BetterClickPPT.exe'),
        (Join-Path $root 'src\BetterClickPPT.App\bin\Release\net8.0-windows\BetterClickPPT.exe'),
        (Join-Path $root 'src\BetterClickPPT.App\bin\Debug\net8.0-windows\BetterClickPPT.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $AppPath -or -not (Test-Path $AppPath)) {
    throw '未找到 BetterClickPPT.exe，请先运行 build\build.ps1，或用 -AppPath 指定路径。'
}

$AppPath = (Resolve-Path $AppPath).Path
Write-Host "待验证程序：$AppPath"

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

function Find-Containing($scope, [string]$text) {
    $all = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        $name = $element.Current.Name
        if ($name -and $name.Contains($text)) { return $element }
    }

    return $null
}

function Get-ComboBoxValue($window, [string]$namePart) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)

    foreach ($combo in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($combo.Current.Name -and $combo.Current.Name.Contains($namePart)) {
            try {
                return $combo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            }
            catch {
                return ''
            }
        }
    }

    return $null
}

function Select-Tab($window, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    foreach ($item in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        if ($item.Current.Name -eq $name) {
            $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $pattern.Select()
            Start-Sleep -Milliseconds 700
            return $true
        }
    }

    return $false
}

$process = $null
try {
    $process = Start-Process -FilePath $AppPath -ArgumentList '--settings' -PassThru
    Start-Sleep -Seconds 4

    $rootElement = [System.Windows.Automation.AutomationElement]::RootElement

    # 托盘程序 + 设置窗口都在同一个进程里，这里按标题找设置窗口
    $deadline = (Get-Date).AddSeconds(15)
    $window = $null
    while ((Get-Date) -lt $deadline -and $null -eq $window) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        foreach ($candidate in $rootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($candidate.Current.Name -like '*设置*') { $window = $candidate; break }
        }
        if ($null -eq $window) { Start-Sleep -Milliseconds 400 }
    }

    Assert ($null -ne $window) '设置窗口已打开（--settings）'
    if ($null -eq $window) { throw '未找到设置窗口。' }

    # --- 目标软件页（默认选中）---
    Assert ($null -ne (Find-Containing $window '启用触摸增强（总开关）')) '目标软件页显示总开关'
    Assert ($null -ne (Find-Containing $window 'Microsoft PowerPoint')) '目标软件页显示 PowerPoint 勾选项'
    Assert ($null -ne (Find-Containing $window 'WPS 演示')) '目标软件页显示 WPS 演示勾选项'

    # --- 翻页行为页 ---
    Assert (Select-Tab $window '翻页行为') '可以切换到“翻页行为”标签页'
    Assert ($null -ne (Find-Containing $window '触摸 / 手写笔轻触')) '翻页行为页显示触摸开关'
    Assert ($null -ne (Find-Containing $window '轻触的处理方式')) '翻页行为页存在“轻触的处理方式”'
    $touchActionValue = Get-ComboBoxValue $window '轻触的处理方式'
    Write-Host ("  轻触的处理方式当前值：" + $touchActionValue)
    Assert ($touchActionValue -like '*模拟鼠标点击*') '轻触动作默认是“模拟鼠标点击”'
    Assert ((Get-ComboBoxValue $window '翻页按键') -ne $null) '存在“翻页按键”下拉框'
    Assert ((Get-ComboBoxValue $window '发送方式') -ne $null) '存在“发送方式”下拉框'
    Assert ($null -ne (Find-Containing $window '深度触控模式')) '翻页行为页显示深度触控模式'
    Assert ($null -ne (Find-Containing $window '恢复默认设置')) '翻页行为页存在“恢复默认设置”'

    # --- 诊断页 ---
    Assert (Select-Tab $window '诊断') '可以切换到“诊断”标签页'
    Assert ($null -ne (Find-Containing $window '检测当前前台窗口')) '诊断页存在“检测当前前台窗口”'
    Assert ($null -ne (Find-Containing $window '实时活动')) '诊断页显示实时活动列表'

    Assert ($null -ne (Find-Containing $window '保存并立即生效')) '存在“保存并立即生效”按钮'
}
catch {
    Write-Host ('  [异常] ' + $_.Exception.Message) -ForegroundColor Red
    $results.Add('FAIL 执行异常：' + $_.Exception.Message)
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 800

    Write-Host ''
    Write-Host '=================================================='
    Write-Host ' 验证结论'
    Write-Host '=================================================='
    $results | ForEach-Object { Write-Host ('  ' + $_) }

    $failureCount = @($results | Where-Object { $_ -like 'FAIL*' }).Count
    Write-Host ''
    if ($failureCount -eq 0) {
        Write-Host '设置窗口验证通过。' -ForegroundColor Green
    }
    else {
        Write-Host "设置窗口验证失败 $failureCount 项。" -ForegroundColor Red
    }

    exit $failureCount
}
