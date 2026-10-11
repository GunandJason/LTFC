#requires -Version 5.1
<#
    BetterClickPPT 一键构建脚本。

    做四件事：
      1. 生成应用图标（如缺失）；
      2. 编译整个解决方案（Release）；
      3. 运行自测程序（单元测试 + 端到端测试，可用 -SkipTests 跳过）；
      4. 发布单文件 exe 到 dist\ ：
         - dist\BetterClickPPT.exe         自包含单文件（无需安装 .NET，约 60~80MB）
         - dist\BetterClickPPT-Setup.exe   同一文件的副本，作为“安装程序”分发
         - dist\lite\BetterClickPPT.exe    框架依赖版（约 1MB，需要 .NET 8 桌面运行时）

    用法：
      powershell -ExecutionPolicy Bypass -File build\build.ps1
      powershell -ExecutionPolicy Bypass -File build\build.ps1 -SkipTests
      powershell -ExecutionPolicy Bypass -File build\build.ps1 -NoLite
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$NoLite  # 不生成“框架依赖”精简版
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$solution = Join-Path $root 'BetterClickPPT.sln'
$appProject = Join-Path $root 'src\BetterClickPPT.App\BetterClickPPT.App.csproj'
$iconPath = Join-Path $root 'assets\BetterClickPPT.ico'

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan
}

function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) {
        throw "$what 失败（退出码 $LASTEXITCODE）。"
    }
}

Write-Step "1/5 准备图标"
if (-not (Test-Path $iconPath)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1')
    Assert-ExitCode '生成图标'
}
else {
    Write-Host "已存在：$iconPath"
}

Write-Step "2/5 编译解决方案（$Configuration）"
Push-Location $root
try {
    & dotnet build $solution -c $Configuration --nologo
    Assert-ExitCode '编译'
}
finally {
    Pop-Location
}

if (-not $SkipTests) {
    Write-Step "3/5 运行自测（单元 + 端到端）"
    $selfTest = Join-Path $root "tests\BetterClickPPT.SelfTest\bin\$Configuration\net8.0-windows\BetterClickPPT.SelfTest.exe"
    if (-not (Test-Path $selfTest)) {
        throw "未找到自测程序：$selfTest"
    }

    & $selfTest
    if ($LASTEXITCODE -ne 0) {
        throw '自测未通过，已停止发布。'
    }
}
else {
    Write-Step '3/5 已跳过自测（-SkipTests）'
}

Write-Step '4/5 发布自包含单文件版'
if (Test-Path $dist) {
    Get-ChildItem $dist -Recurse -File | Remove-Item -Force
}
else {
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
}

Push-Location $root
try {
    $publishArgs = @(
        'publish', $appProject,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=none',
        '-p:PublishReadyToRun=false',
        '-o', $dist
    )
    & dotnet @publishArgs --nologo
    Assert-ExitCode '发布'
}
finally {
    Pop-Location
}

$mainExe = Join-Path $dist 'BetterClickPPT.exe'
if (-not (Test-Path $mainExe)) {
    throw "发布结果缺少 BetterClickPPT.exe（$dist）"
}

Copy-Item $mainExe (Join-Path $dist 'BetterClickPPT-Setup.exe') -Force

# 附带一份使用说明，方便单独分发 exe
$usageSource = Join-Path $root 'docs\使用说明.md'
if (Test-Path $usageSource) {
    Copy-Item $usageSource (Join-Path $dist '使用说明.txt') -Force
}

if (-not $NoLite) {
    Write-Step '4.5/5 发布框架依赖精简版'
    $liteDir = Join-Path $dist 'lite'
    New-Item -ItemType Directory -Path $liteDir -Force | Out-Null

    Push-Location $root
    try {
        $liteArgs = @(
            'publish', $appProject,
            '-c', $Configuration,
            '-r', 'win-x64',
            '--self-contained', 'false',
            '-p:PublishSingleFile=true',
            '-p:DebugType=none',
            '-o', $liteDir
        )
        & dotnet @liteArgs --nologo
        Assert-ExitCode '发布精简版'
    }
    finally {
        Pop-Location
    }
}

Write-Step '5/5 产物汇总'
Get-ChildItem $dist -Recurse -File | Sort-Object -Property Length -Descending | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    Write-Host ('  {0,-46} {1,10:n0} 字节  SHA256:{2}' -f $_.FullName.Replace($dist, 'dist'), $_.Length, $hash)
}

Write-Host ''
Write-Host '构建完成。' -ForegroundColor Green
Write-Host '分发方式：把 dist\BetterClickPPT-Setup.exe 发给用户，双击即可安装（无需管理员权限）。' -ForegroundColor Green
Write-Host '真实环境验证：powershell -ExecutionPolicy Bypass -File tools\verify-powerpoint.ps1 -AppPath dist\BetterClickPPT.exe'
