# build.ps1 —— OpenChoose 一键构建
#   1) 生成应用图标（如不存在）
#   2) 编译 PPTChooser.exe（目标框架 .NET Framework 4.5）
#   3) 用 WiX 打包 OpenChoose.msi（每用户安装，无需管理员）
#   4) 可选：运行冒烟测试与安装/卸载测试
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Test

param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src\PPTChooser\PPTChooser.csproj'
$exe = Join-Path $root 'src\PPTChooser\bin\Release\PPTChooser.exe'
$icon = Join-Path $root 'src\PPTChooser\Resources\app.ico'
$msi = Join-Path $dist 'OpenChoose.msi'

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

Write-Host '[1/4] 生成图标 ...'
if (-not (Test-Path $icon)) {
    & (Join-Path $root 'tools\make-icon.ps1') -OutputPath $icon | Write-Host
} else {
    Write-Host ('      已存在: ' + $icon)
}

Write-Host '[2/4] 编译 PPTChooser.exe (.NET Framework 4.5) ...'
& dotnet build $project -c Release -v m
if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败' }
if (-not (Test-Path $exe)) { throw ('未生成 ' + $exe) }
Write-Host ('      输出: ' + $exe + ' (' + (Get-Item $exe).Length + ' 字节)')

Write-Host '[3/4] 打包 OpenChoose.msi (WiX) ...'
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw '未找到 wix 命令。请先安装 WiX Toolset 5： dotnet tool install --global wix --version 5.*'
}

# 安装向导素材（许可协议文本、横幅图片），随构建自动生成
& (Join-Path $root 'tools\make-license-rtf.ps1') | Out-Null
& (Join-Path $root 'tools\make-banner.ps1') | Out-Null

# 安装向导（UI）与关闭运行中程序（Util）需要这两个扩展，缺失时自动下载
$extensions = @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')
$installed = (wix extension list -g) -join "`n"
foreach ($extension in $extensions) {
    if ($installed -notmatch [regex]::Escape($extension)) {
        Write-Host ('      安装 WiX 扩展 ' + $extension + ' ...')
        & wix extension add -g ($extension + '/5.0.2')
        if ($LASTEXITCODE -ne 0) { throw ('安装扩展失败: ' + $extension) }
    }
}

# WiX 按“当前工作目录”解析相对路径，因此必须在 installer 目录下执行
Push-Location (Join-Path $root 'installer')
try {
    & wix build 'Product.wxs' -o $msi -arch x86 `
        -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -culture zh-CN
    if ($LASTEXITCODE -ne 0) { throw 'wix build 失败' }
} finally {
    Pop-Location
}
Write-Host ('      输出: ' + $msi + ' (' + (Get-Item $msi).Length + ' 字节)')

Write-Host '[4/4] 校验构建结果 ...'
$report = Join-Path $dist 'build-selftest.txt'
Start-Process -FilePath $exe -ArgumentList @('--selftest', ('"' + $report + '"'), '--silent') -Wait | Out-Null
if (-not (Test-Path $report)) { throw '程序自检失败' }
$text = Get-Content $report -Raw
if (-not $text.Contains('.NETFramework,Version=v4.5')) { throw '目标框架不是 .NET 4.5' }
Write-Host ('      自检通过: 目标框架 .NET 4.5；报告 ' + $report)

Write-Host ''
Write-Host '构建完成：'
Write-Host ('  程序  : ' + $exe)
Write-Host ('  安装包: ' + $msi)

if ($Test) {
    Write-Host ''
    Write-Host '运行冒烟测试 (tools\test-smoke.ps1) ...'
    & (Join-Path $root 'tools\test-smoke.ps1')
    Write-Host '运行安装/卸载测试 (tools\check-msi.ps1) ...'
    & (Join-Path $root 'tools\check-msi.ps1')
}
