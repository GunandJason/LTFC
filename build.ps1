# build.ps1 —— LTFC 一键构建与自测
#
# 流程：
#   1) 生成图标（如缺失）
#   2) 编译解决方案（LTFC + ClassTell + OpenChoose）
#   3) 运行各模块自测（可 -SkipTests 跳过）
#   4) 发布到 dist\（LTFC.exe + 子程序 + 文档）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$SkipVideoBundling
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$solution = Join-Path $root 'LTFC.sln'
$icon = Join-Path $root 'assets\LTFC.ico'

function Step([string]$text) {
    Write-Host ''
    Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan
}

function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw ($what + ' 失败（退出码 ' + $LASTEXITCODE + '）') }
}

Step '1/4 准备图标'
if (-not (Test-Path $icon)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1')
    Assert-ExitCode '生成图标'
} else {
    Write-Host ('已存在：' + $icon)
}

Step ('2/4 编译解决方案（' + $Configuration + '）')
& dotnet build $solution -c $Configuration --nologo
Assert-ExitCode '编译'

if (-not $SkipTests) {
    Step '3/4 运行自测'

    # LTFC：界面/设置/模块管理自测
    $ltfcSelfTest = Join-Path $root ('src\LTFC\LTFC.SelfTest\bin\' + $Configuration + '\net48\LTFC.SelfTest.exe')
    if (Test-Path $ltfcSelfTest) {
        & $ltfcSelfTest
        Assert-ExitCode 'LTFC 自测'
    } else {
        Write-Host ('未找到 LTFC 自测程序，跳过：' + $ltfcSelfTest) -ForegroundColor Yellow
    }

    # ClassTell：邮箱指令通知自测
    $classTellSelfTest = Join-Path $root ('src\ClassTell\tests\ClassTell.SelfTest\bin\' + $Configuration + '\net48\ClassTell.SelfTest.exe')
    if (Test-Path $classTellSelfTest) {
        & $classTellSelfTest
        Assert-ExitCode 'ClassTell 自测'
    } else {
        Write-Host ('未找到 ClassTell 自测程序，跳过：' + $classTellSelfTest) -ForegroundColor Yellow
    }

} else {
    Step '3/4 已跳过自测（-SkipTests）'
}

if (-not $SkipPublish) {
    Step '4/4 发布到 dist（内置全部模块，用户无需另外下载）'

    # dist 里的 LTFC 还在运行时无法覆盖（文件被占用）：先礼貌结束它自己发布出来的这份副本
    $runningFromDist = @(Get-Process -Name 'LTFC' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) })
    if ($runningFromDist.Count -gt 0) {
        Write-Host ('检测到 dist 里的 LTFC 正在运行（PID ' + ($runningFromDist.Id -join ', ') + '），先结束它再发布…') -ForegroundColor Yellow
        $runningFromDist | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
    }

    if (Test-Path $dist) { Get-ChildItem $dist -Recurse -File | Remove-Item -Force }
    else { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

    $ltfcOut = Join-Path $root ('src\LTFC\LTFC.App\bin\' + $Configuration + '\net48')
    if (-not (Test-Path (Join-Path $ltfcOut 'LTFC.exe'))) { throw ('未找到 LTFC.exe：' + $ltfcOut) }
    Copy-Item (Join-Path $ltfcOut '*') $dist -Recurse -Force
    Write-Host '已发布主程序：LTFC.exe（含 assets：赞赏码、图标）'

    # ---- 内置子程序：每个模块放在独立子目录，避免与主程序的依赖版本互相覆盖 ----
    $bundles = @(
        @{ Name = 'ClassTell';      Source = ('src\ClassTell\bin\' + $Configuration + '\net48');                                       Target = 'modules\ClassTell';      Exe = 'ClassTell.exe' },
        @{ Name = 'OpenChoose';     Source = ('src\OpenChoose\src\PPTChooser\bin\' + $Configuration);                                 Target = 'modules\OpenChoose';     Exe = 'PPTChooser.exe' }
    )

    foreach ($bundle in $bundles) {
        $source = Join-Path $root $bundle.Source
        $exe = Join-Path $source $bundle.Exe
        if (-not (Test-Path $exe)) {
            Write-Host ('提示：未找到 ' + $bundle.Name + '，跳过（' + $exe + '）') -ForegroundColor Yellow
            continue
        }

        $target = Join-Path $dist $bundle.Target
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Get-ChildItem $source -File | Where-Object { $_.Extension -ne '.pdb' } | Copy-Item -Destination $target -Force
        Write-Host ('已内置模块：' + $bundle.Name + '  →  ' + $bundle.Target)
    }

    # ---- AutoNotice 的配置文件（command.txt）----
    $commandSource = Join-Path $root 'src\AutoNotice\command.txt'
    if (Test-Path $commandSource) {
        $commandTarget = Join-Path $dist 'AutoNotice'
        New-Item -ItemType Directory -Path $commandTarget -Force | Out-Null
        Copy-Item $commandSource $commandTarget -Force
        Write-Host '已内置 AutoNotice 配置：AutoNotice\command.txt'
    } else {
        Write-Host '提示：未找到 src\AutoNotice\command.txt，AutoNotice 首次运行会自动生成模板' -ForegroundColor Yellow
    }

    # ---- 视频下载（4K Video Downloader Plus 便携版，随程序一起发布）----
    if ($SkipVideoBundling) {
        Write-Host '已跳过视频下载打包（-SkipVideoBundling）' -ForegroundColor Yellow
    } else {
        $videoSource = $null
        $desktop = [Environment]::GetFolderPath('Desktop')
        if (Test-Path $desktop) {
            foreach ($candidate in (Get-ChildItem $desktop -Directory -Filter '4KVideoDownloader*' -ErrorAction SilentlyContinue)) {
                if (Test-Path (Join-Path $candidate.FullName '4KVideoDownloaderPlusPortable.exe')) {
                    $videoSource = $candidate.FullName
                    break
                }
            }
        }

        if ($videoSource) {
            $videoTarget = Join-Path $dist 'VideoDownload'
            New-Item -ItemType Directory -Path $videoTarget -Force | Out-Null
            Write-Host ('正在内置视频下载工具：' + $videoSource)
            Copy-Item (Join-Path $videoSource '*') $videoTarget -Recurse -Force
            $videoSize = (Get-ChildItem $videoTarget -Recurse -File | Measure-Object -Property Length -Sum).Sum
            Write-Host ('已内置视频下载：VideoDownload（' + [math]::Round($videoSize / 1MB, 1) + ' MB）')
        } else {
            Write-Host '提示：桌面没有找到 4KVideoDownloader* 便携版目录，视频下载模块会自动使用该目录作为备用路径' -ForegroundColor Yellow
        }
    }

    Write-Host ''
    Write-Host '产物（按目录）：' -ForegroundColor Green
    $groups = Get-ChildItem $dist -Recurse -File | Group-Object {
        $relative = $_.FullName.Substring($dist.Length).TrimStart('\')
        $index = $relative.IndexOf('\')
        if ($index -lt 0) { '(dist 根目录)' } else { $relative.Substring(0, $index) }
    } | Sort-Object Name

    foreach ($entry in $groups) {
        $size = [math]::Round((($entry.Group | Measure-Object -Property Length -Sum).Sum) / 1MB, 1)
        Write-Host ('  {0,-22} {1,5} 个文件 {2,10} MB' -f $entry.Name, $entry.Count, $size)
    }
} else {
    Step '4/4 已跳过发布（-SkipPublish）'
}

Write-Host ''
Write-Host '构建完成。' -ForegroundColor Green
