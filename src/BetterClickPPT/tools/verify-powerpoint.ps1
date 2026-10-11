#requires -Version 5.1
<#
    真实 PowerPoint 端到端验证脚本。

    验证内容：
      1. 自动检测 PowerPoint 安装位置（--detect）；
      2. 生成演示文稿并进入全屏放映；
      3. 确认识别条件成立（--probe：screenClass + 全屏 + 无边框 + 已命中规则）；
      4. 定位放映窗口并切到前台（真实放映场景）；
      5. 触摸点击 → 页码正好前进 1 页（“发送下一页按键”模式）；
      6. 普通鼠标点击仍按原有方式翻页；
      7. 发送方式改为窗口消息（PostMessage）后同样只前进 1 页；
      8. 默认的“模拟鼠标点击”模式：分步动画逐帧推进、动画播完才翻页、
         轻触超链接被原样还原（证明屏幕上的“上一页 / 下一页”按钮同理可用）。

    用法：
      powershell -ExecutionPolicy Bypass -File tools\verify-powerpoint.ps1
      powershell -ExecutionPolicy Bypass -File tools\verify-powerpoint.ps1 -AppPath "dist\BetterClickPPT.exe"
#>
param(
    [string]$AppPath = '',
    [int]$WaitAfterClickMs = 1000
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

Add-Type -Path (Join-Path $PSScriptRoot 'BcppInjector.cs')
Add-Type -AssemblyName System.Windows.Forms
[BcppInjector]::MakeDpiAware()

# ---------------------------------------------------------------- 注入自检
# 某些情况下桌面输入队列会被其它程序长期占用，SendInput 会一直阻塞；
# 这里先用一个带超时的后台任务探测，避免脚本本身卡死。
function Test-Injection([int]$timeoutSeconds = 6) {
    $job = Start-Job -ScriptBlock {
        param($path)
        Add-Type -Path $path
        [BcppInjector]::Click(10, 10, $true) | Out-Null
        'OK'
    } -ArgumentList (Join-Path $PSScriptRoot 'BcppInjector.cs')

    $done = Wait-Job $job -Timeout $timeoutSeconds
    $result = $null
    if ($done) { $result = Receive-Job $job }
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    return ($result -contains 'OK')
}

if (-not (Test-Injection)) {
    Write-Host '无法注入输入：桌面输入队列被其它程序占用（SendInput 被阻塞）。' -ForegroundColor Red
    Write-Host '请先结束相关进程或重启电脑（关闭 VS Code / 终端也可释放），然后重新运行本脚本。' -ForegroundColor Yellow
    exit 2
}

Write-Host '输入注入自检通过。'

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

$configPath = Join-Path $env:LOCALAPPDATA 'BetterClickPPT\config.json'
$configBackup = $null
if (Test-Path $configPath) {
    $configBackup = Get-Content -Raw -Encoding UTF8 $configPath
}

function Set-BcppConfig([string]$sendMode, [string]$touchAction = 'MouseClick') {
    $directory = Split-Path -Parent $configPath
    if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

    $json = [ordered]@{
        SchemaVersion       = 1
        Enabled             = $true
        AdvanceOnTouch      = $true
        AdvanceOnMouse      = $false
        TouchAction         = $touchAction
        SwallowTouchClick   = $true
        SwallowMouseClick   = $true
        RawTouchEnabled     = $false
        EdgeMarginPx        = 0
        DebounceMs          = 400
        NextKey             = 'PageDown'
        KeySendMode         = $sendMode
        TapMaxDurationMs    = 400
        TapMaxMovementRatio = 0.02
        ShowNotifications   = $false
        VerboseLog          = $true
        LogMaxKb            = 4096
        AutoStart           = $false
        Targets             = @(
            [ordered]@{
                Id                = 'powerpoint'
                DisplayName       = 'Microsoft PowerPoint'
                Enabled           = $true
                IsBuiltIn         = $true
                ProcessNames      = @('POWERPNT.EXE')
                ClassNames        = @('screenClass')
                RequireFullScreen = $true
                RequireBorderless = $true
            }
        )
    }

    $json | ConvertTo-Json -Depth 8 | Set-Content -Path $configPath -Encoding UTF8
}

function Start-BcppHelper() {
    $process = Start-Process -FilePath $AppPath -ArgumentList '--tray' -PassThru
    Start-Sleep -Milliseconds 1800
    return $process
}

function Stop-BcppHelper($process) {
    if ($null -eq $process) { return }

    try {
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit(3000) | Out-Null
        }
    }
    catch {
    }
}

function Get-ShowPosition($show) {
    try {
        return [int]$show.View.CurrentShowPosition
    }
    catch {
        Write-Host ('  [警告] 读取页码失败：' + $_.Exception.Message) -ForegroundColor Yellow
        return -1
    }
}

# ---------------------------------------------------------------- 开始验证
$powerPoint = $null
$helper = $null
$presentationPath = Join-Path $env:TEMP ('bcpp-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.pptx')

try {
    Write-Host ''
    Write-Host '--- 1. 检测 PowerPoint 安装位置 ---'
    $detectOutput = (& $AppPath --detect 2>&1 | Out-String)
    $pptLine = ($detectOutput -split "`r?`n" | Where-Object { $_ -match 'PowerPoint\s*:' } | Select-Object -First 1)
    if ($pptLine) { Write-Host ('  ' + $pptLine.Trim()) }
    Assert ($pptLine -match '已找到') 'PowerPoint 安装位置已自动检测到'

    Write-Host ''
    Write-Host '--- 2. 生成演示文稿并开始全屏放映 ---'
    $powerPoint = New-Object -ComObject PowerPoint.Application
    $presentation = $powerPoint.Presentations.Add()
    for ($i = 1; $i -le 6; $i++) {
        $slide = $presentation.Slides.Add($i, 2)
        $slide.Shapes.Title.TextFrame.TextRange.Text = "第 $i 页"
        $slide.Shapes.Item(2).TextFrame.TextRange.Text = 'BetterClickPPT 验证'
    }

    $presentation.SaveAs($presentationPath)
    $show = $presentation.SlideShowSettings.Run()
    Start-Sleep -Milliseconds 2000
    $show.Activate()
    Start-Sleep -Milliseconds 1000

    Write-Host ("  当前页码：" + $show.View.CurrentShowPosition)
    Assert ($show.View.CurrentShowPosition -eq 1) '放映已开始且位于第 1 页'

    Write-Host ''
    Write-Host '--- 3. 检查放映窗口的识别条件 ---'
    $probeOutput = (& $AppPath --probe 2>&1 | Out-String)
    ($probeOutput -split "`r?`n" | Where-Object { $_ -match 'POWERPNT|已命中|命中规则|全屏=' } | ForEach-Object { Write-Host ('  ' + $_.Trim()) })
    Assert ($probeOutput -match 'screenClass') '放映窗口类名为 screenClass'
    Assert ($probeOutput -match '已命中') '放映窗口被识别为放映窗口'

    Write-Host ''
    Write-Host '--- 4. 定位放映窗口并切到前台 ---'
    $showWindow = [BcppInjector]::FindShowWindow('screenClass')
    Assert ($showWindow -ne [IntPtr]::Zero) '已找到类名为 screenClass 的放映窗口'
    [BcppInjector]::ForceForeground($showWindow) | Out-Null
    Start-Sleep -Milliseconds 600
    Assert ([BcppInjector]::IsForeground($showWindow)) '放映窗口已处于前台（真实放映场景）'

    $bounds = [BcppInjector]::GetWindowBounds($showWindow)
    $centerX = [int]($bounds[0] + $bounds[2] / 2)
    $centerY = [int]($bounds[1] + $bounds[3] / 2)
    Write-Host ("  放映窗口：{0},{1} {2}x{3}，点击点：{4},{5}" -f $bounds[0], $bounds[1], $bounds[2], $bounds[3], $centerX, $centerY)

    Write-Host ''
    Write-Host '--- 5. 发送下一页按键模式（放映窗口在前台 → 模拟按键）：触摸点击翻页 ---'
    Set-BcppConfig -sendMode 'Auto' -touchAction 'NextKey'
    $helper = Start-BcppHelper
    [BcppInjector]::ForceForeground($showWindow) | Out-Null
    Start-Sleep -Milliseconds 500

    $before = Get-ShowPosition $show
    [BcppInjector]::Click($centerX, $centerY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $after = Get-ShowPosition $show
    Write-Host ("  页码：$before -> $after")
    Assert ($after -eq ($before + 1)) '一次触摸点击只前进 1 页（翻页且原始点击被吞掉）'

    $before = $after
    [BcppInjector]::Click($centerX, $centerY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $after = Get-ShowPosition $show
    Write-Host ("  页码：$before -> $after")
    Assert ($after -eq ($before + 1)) '再次触摸点击仍然只前进 1 页'

    Write-Host ''
    Write-Host '--- 6. 普通鼠标点击仍按原有方式翻页 ---'
    $before = $after
    [BcppInjector]::Click($centerX, $centerY, $false) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $after = Get-ShowPosition $show
    Write-Host ("  页码：$before -> $after")
    Assert ($after -eq ($before + 1)) '普通鼠标点击仍能翻页（未被拦截，也没有多翻）'

    Write-Host ''
    Write-Host '--- 7. 发送下一页按键 + 窗口消息（放映窗口不在前台时的降级路径）---'
    Set-BcppConfig -sendMode 'PostMessage' -touchAction 'NextKey'
    Stop-BcppHelper $helper
    $helper = Start-BcppHelper
    Start-Sleep -Milliseconds 400

    $before = Get-ShowPosition $show
    [BcppInjector]::Click($centerX, $centerY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $after = Get-ShowPosition $show
    Write-Host ("  页码：$before -> $after")
    Assert ($after -eq ($before + 1)) '窗口消息方式同样只前进 1 页'

    # ==================================================================
    # 8. 分步动画 / 超链接（“模拟鼠标点击”模式，默认）
    # ==================================================================
    Write-Host ''
    Write-Host '--- 8. 分步动画与超链接（模拟鼠标点击模式）---'

    $animationPath = Join-Path $env:TEMP ('bcpp-anim-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.pptx')
    $animPresentation = $powerPoint.Presentations.Add()
    for ($i = 1; $i -le 4; $i++) {
        $slide = $animPresentation.Slides.Add($i, 2)
        $slide.Shapes.Title.TextFrame.TextRange.Text = "第 $i 页"
        $slide.Shapes.Item(2).TextFrame.TextRange.Text = 'BetterClickPPT 动画验证'
    }

    # 第 1 页：两个“单击时出现”的动画（msoAnimEffectAppear=1，单击触发=1）
    $firstSlide = $animPresentation.Slides.Item(1)
    $shapeA = $firstSlide.Shapes.AddShape(1, 120, 400, 260, 120)
    $shapeA.TextFrame.TextRange.Text = '动画 1'
    $shapeB = $firstSlide.Shapes.AddShape(1, 420, 400, 260, 120)
    $shapeB.TextFrame.TextRange.Text = '动画 2'
    $firstSlide.TimeLine.MainSequence.AddEffect($shapeA, 1, 0, 1) | Out-Null
    $firstSlide.TimeLine.MainSequence.AddEffect($shapeB, 1, 0, 1) | Out-Null

    # 第 2 页：一个铺满整页的链接矩形 → 点击它应跳到第 4 页（而不是第 3 页）
    $secondSlide = $animPresentation.Slides.Item(2)
    $linkShape = $secondSlide.Shapes.AddShape(1, 0, 0, $animPresentation.PageSetup.SlideWidth, $animPresentation.PageSetup.SlideHeight)
    $linkShape.Fill.Visible = 0
    $linkShape.Line.Visible = 0
    $linkShape.ActionSettings(1).Hyperlink.SubAddress = '4'

    $animPresentation.SaveAs($animationPath)
    $animShow = $animPresentation.SlideShowSettings.Run()
    Start-Sleep -Milliseconds 2000
    $animShow.Activate()
    Start-Sleep -Milliseconds 800

    $animWindow = [BcppInjector]::FindShowWindow('screenClass')
    [BcppInjector]::ForceForeground($animWindow) | Out-Null
    Start-Sleep -Milliseconds 600

    $animBounds = [BcppInjector]::GetWindowBounds($animWindow)
    $animCenterX = [int]($animBounds[0] + $animBounds[2] / 2)
    $animCenterY = [int]($animBounds[1] + $animBounds[3] / 2)

    Set-BcppConfig -sendMode 'Auto' -touchAction 'MouseClick'
    Stop-BcppHelper $helper
    $helper = Start-BcppHelper
    Start-Sleep -Milliseconds 400

    Write-Host ("  起始：页码=" + (Get-ShowPosition $animShow) + "，动画索引=" + [int]$animShow.View.GetClickIndex())

    $beforePosition = Get-ShowPosition $animShow
    $beforeClick = [int]$animShow.View.GetClickIndex()
    [BcppInjector]::Click($animCenterX, $animCenterY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $afterPosition = Get-ShowPosition $animShow
    $afterClick = [int]$animShow.View.GetClickIndex()
    Write-Host ("  轻触后：页码=$afterPosition，动画索引=$afterClick")
    Assert ($afterPosition -eq $beforePosition) '第 1 次轻触应播放页内动画，而不是直接翻页'
    Assert ($afterClick -eq ($beforeClick + 1)) '第 1 次轻触应让动画前进一帧'

    $beforeClick = $afterClick
    [BcppInjector]::Click($animCenterX, $animCenterY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $afterClick = [int]$animShow.View.GetClickIndex()
    Write-Host ("  再轻触：页码=" + (Get-ShowPosition $animShow) + "，动画索引=$afterClick")
    Assert ($afterClick -eq ($beforeClick + 1)) '第 2 次轻触应让动画再前进一帧'

    $beforePosition = Get-ShowPosition $animShow
    [BcppInjector]::Click($animCenterX, $animCenterY, $true) | Out-Null
    Start-Sleep -Milliseconds $WaitAfterClickMs
    $afterPosition = Get-ShowPosition $animShow
    Write-Host ("  动画播完后轻触：页码=$beforePosition -> $afterPosition")
    Assert ($afterPosition -eq ($beforePosition + 1)) '动画播完后轻触才翻到下一页'

    # 第 2 页整页都是超链接：轻触应触发超链接跳到第 4 页（证明点击被原样还原，上一页按钮同理可用）
    if ($afterPosition -eq 2) {
        $null = $animShow.View.GotoSlide(2)
        Start-Sleep -Milliseconds 600
        [BcppInjector]::Click($animCenterX, $animCenterY, $true) | Out-Null
        Start-Sleep -Milliseconds $WaitAfterClickMs
        $linkPosition = Get-ShowPosition $animShow
        Write-Host ("  轻触超链接后的页码：$linkPosition（期望 4）")
        Assert ($linkPosition -eq 4) '轻触超链接被原样还原为鼠标点击（上一页 / 下一页按钮同理可用）'
    }
    else {
        Write-Host '  [跳过] 未能回到第 2 页，跳过超链接验证'
    }

    try {
        $animShow.View.Exit()
        $animPresentation.Close()
    }
    catch {
    }

    Remove-Item $animationPath -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Host ('  [异常] ' + $_.Exception.Message) -ForegroundColor Red
    $results.Add('FAIL 执行异常：' + $_.Exception.Message)
}
finally {
    Stop-BcppHelper $helper

    try {
        if ($powerPoint) {
            foreach ($window in @($powerPoint.SlideShowWindows)) {
                $window.View.Exit()
            }
            $powerPoint.Quit()
        }
    }
    catch {
    }

    Start-Sleep -Milliseconds 500
    Remove-Item $presentationPath -Force -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host '=================================================='
    Write-Host ' 验证结论'
    Write-Host '=================================================='
    $results | ForEach-Object { Write-Host ('  ' + $_) }

    $failed = @($results | Where-Object { $_ -like 'FAIL*' }).Count
    Write-Host ''
    if ($failed -eq 0) {
        Write-Host 'PowerPoint 真实环境验证通过。' -ForegroundColor Green
    }
    else {
        Write-Host "PowerPoint 真实环境验证失败 $failed 项。" -ForegroundColor Red
        Write-Host ("日志：" + (Join-Path $env:LOCALAPPDATA 'BetterClickPPT\logs\betterclickppt.log'))
    }

    if ($null -ne $configBackup) {
        Set-Content -Path $configPath -Value $configBackup -Encoding UTF8
        Write-Host '已恢复原有配置文件。'
    }
    else {
        Remove-Item $configPath -Force -ErrorAction SilentlyContinue
    }

    exit $failed
}
