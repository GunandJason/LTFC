#requires -Version 5.1
<#
    Office「触摸 / 鼠标模式」探查脚本（只读探查，结束时关闭自己启动的 PowerPoint）。

    用途：确认 PowerPoint（或 WPS 演示）里“触摸 / 鼠标模式”开关在 UI Automation 中的
    名称、AutomationId 与开关状态，为“自动切回鼠标模式”的实现提供依据。

    脚本自己创建测试文稿并启动一个可见的 PowerPoint 窗口，探查完成后强制结束该进程，
    不会修改用户的任何配置。

    用法：
      powershell -ExecutionPolicy Bypass -File tools\probe-office-touchmode.ps1
      powershell -ExecutionPolicy Bypass -File tools\probe-office-touchmode.ps1 -Exe wpp.exe
#>
param(
    [string]$Exe = 'powerpnt.exe',
    [int]$StartupTimeoutSeconds = 45,
    [int]$MaxHits = 60,
    [int]$MaxButtons = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Resolve-ExePath([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\' + $name.ToLowerInvariant()
    $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
    if ($item -and $item.'(default)') { return $item.'(default)' }

    foreach ($candidate in @(
            (Join-Path $env:ProgramFiles ('Microsoft Office\root\Office16\' + $name)),
            (Join-Path ${env:ProgramFiles(x86)} ('Microsoft Office\root\Office16\' + $name)))) {
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

$exePath = Resolve-ExePath $Exe
if (-not $exePath) { throw "找不到可执行文件：$Exe" }

Write-Host "目标程序：$exePath"
Write-Host ''

# ---------------------------------------------------------------- 生成测试文稿
$deck = Join-Path $env:TEMP ('bcpp_probe_' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.pptx')
$progId = if ($Exe -like '*wpp*') { 'KWPP.Application' } else { 'PowerPoint.Application' }

$com = $null
try {
    $com = New-Object -ComObject $progId
    $presentation = $com.Presentations.Add()
    $presentation.SaveAs($deck)
    $presentation.Close()
    Write-Host "已生成测试文稿：$deck"
}
catch {
    Write-Host ('跳过文稿生成（COM 不可用：' + $_.Exception.Message + '）')
    $deck = $null
}
finally {
    if ($null -ne $com) {
        try { $com.Quit() } catch { }
        try { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($com) } catch { }
    }
}

# ---------------------------------------------------------------- 启动可见窗口
$arguments = if ($deck) { '"' + $deck + '"' } else { '' }
$process = Start-Process -FilePath $exePath -ArgumentList $arguments -PassThru
Write-Host ("已启动进程 PID = " + $process.Id)

$automationRoot = [System.Windows.Automation.AutomationElement]::RootElement
$pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)

$window = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
while ((Get-Date) -lt $deadline -and $null -eq $window) {
    foreach ($candidate in $automationRoot.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCondition)) {
        if ($candidate.Current.Name) { $window = $candidate; break }
    }

    if ($null -eq $window) { Start-Sleep -Milliseconds 500 }
}

if ($null -eq $window) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw '未找到程序窗口（超时）。'
}

Write-Host ("主窗口：" + $window.Current.Name + "  class=" + $window.Current.ClassName)
Start-Sleep -Seconds 3

try {
    # ------------------------------------------------------------ 命中元素
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host ("元素总数：" + $all.Count)
    Write-Host ''
    Write-Host '--- 名称 / AutomationId / 类名 命中 触摸|触控|Touch|Qat 的元素 ---'

    $hits = 0
    foreach ($element in $all) {
        $name = $element.Current.Name
        $automationId = $element.Current.AutomationId
        $className = $element.Current.ClassName
        $interesting = ($name -match '触摸|触控|触屏|Touch|Mouse Mode') -or
                       ($automationId -match 'Touch|Qat|QAT') -or
                       ($className -match 'Qat|QAT')

        if (-not $interesting) { continue }

        $hits++
        $state = 'Toggle=N/A'
        try {
            $toggle = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $state = 'Toggle=' + $toggle.Current.ToggleState
        }
        catch { }

        $description = '  [命中] name=''{0}'' aid=''{1}'' class=''{2}'' type={3} enabled={4} {5} rect={6}'
        Write-Host ($description -f $name, $automationId, $className, $element.Current.ControlType.ProgrammaticName, $element.Current.IsEnabled, $state, $element.Current.BoundingRectangle)

        if ($hits -ge $MaxHits) { break }
    }

    Write-Host ("命中数量：" + $hits)

    # ------------------------------------------------------------ 按钮概览
    Write-Host ''
    Write-Host '--- 按钮类元素概览（快速访问工具栏 / 功能区）---'
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    Write-Host ("按钮总数：" + $buttons.Count)

    $index = 0
    foreach ($button in $buttons) {
        if ($index -ge $MaxButtons) { break }
        $index++
        $buttonText = "  btn name='{0}' aid='{1}' rect={2}"
        Write-Host ($buttonText -f $button.Current.Name, $button.Current.AutomationId, $button.Current.BoundingRectangle)
    }
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    if ($deck -and (Test-Path $deck)) { Remove-Item $deck -Force -ErrorAction SilentlyContinue }
    Write-Host ''
    Write-Host '已结束探查进程并清理测试文稿。'
}
