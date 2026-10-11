# build-msi.ps1 —— LTFC 安装包（MSI）构建
#
# 流程：
#   1) 检查 WiX 命令行与扩展
#   2) 清理暂存目录 installer\.stage 与输出目录
#   3) 把发布包 dist 暂存到 installer\.stage（与 LTFC.wxs 里的 .stage\... 路径一一对应）
#   4) 暂存内容自检：必需文件在不在、版本对不对、有没有多带东西
#   5) 由 license.txt 生成 license.rtf（ASCII + \u 转义，供许可页显示）
#   6) wix build 生成 MSI
#   7) 产物校验：体积/新鲜度，并用 Windows Installer 直接读 MSI 表核对
#      （安装目录、文件清单、以及“旧 ClassTell 的 UpgradeCode 是否在 Upgrade 表里”）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -IncludeVideoDownload
#   powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -Build            # 先跑一次根目录 build.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -OutputDir release -SkipValidate
#
# 一次性准备（WiX 5.x 是刻意的选择：WiX 6/7 需要接受 Open Source Maintenance Fee EULA）：
#   dotnet tool install -g wix --version 5.0.2
#   wix extension add -g WixToolset.UI.wixext/5.0.2
#   wix extension add -g WixToolset.Util.wixext/5.0.2
#
# 说明：
#   · MSI 的 Version 必须是纯数字（1.0.2），发行标记 _r 只体现在文件名上；
#   · 安装包默认不包含视频下载（约 800 MB），需要时加 -IncludeVideoDownload；
#   · AutoNotice 的 command.txt 刻意不打进安装包（Program Files 普通用户不可写），
#     程序会在 %APPDATA%\LTFC\AutoNotice\command.txt 自动生成模板。
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDir = 'release',
    [string]$Version = '1.0.2',
    [switch]$IncludeVideoDownload,
    [switch]$Build,
    [switch]$SkipValidate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $PSScriptRoot '.stage'
$wxs = Join-Path $PSScriptRoot 'LTFC.wxs'
$licenseTxt = Join-Path $PSScriptRoot 'license.txt'
$licenseRtf = Join-Path $PSScriptRoot 'license.rtf'
$outDir = Join-Path $root $OutputDir
$msiName = 'LTFC-' + $Version + '_r.msi'          # 文件名里的发行标记可以与 MSI 的 Version 不同
$msi = Join-Path $outDir $msiName
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (-not (Test-Path $wix)) { $wix = 'wix' }

# 旧版独立 ClassTell 安装包的 UpgradeCode（取自 src\ClassTell\installer\ClassTell.wxs）：
# 它出现在生成的 MSI 的 Upgrade 表里，就说明“覆盖安装原来的 ClassTell”生效。
$legacyClassTellUpgradeCode = '{7C9A4E1B-2D3F-4A55-9E6C-1B0F5D2A7C33}'

function Step([string]$text) { Write-Host ''; Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan }
function Note([string]$text) { Write-Host ('    ' + $text) }
function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw ($what + ' 失败（退出码 ' + $LASTEXITCODE + '）') }
}

Step '1/7 检查 WiX 与扩展'
& $wix --version | Out-Host
Assert-ExitCode 'wix --version'
# 缺什么装什么（扩展已存在时 wix 会提示已安装，忽略即可）
foreach ($ext in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
    & $wix extension add -g $ext 2>&1 | Out-Null
}
$extensions = @(& $wix extension list -g 2>&1)
foreach ($ext in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
    if ($extensions -match [regex]::Escape($ext)) { Note ('扩展可用：' + $ext) }
    else { throw ('缺少 WiX 扩展：' + $ext + '（wix extension add -g ' + $ext + '）') }
}

Step '2/7 清理暂存目录与输出'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $msi -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $outDir ([IO.Path]::GetFileNameWithoutExtension($msiName) + '.wixpdb')) -Force -ErrorAction SilentlyContinue
New-Item $stage, $outDir -ItemType Directory -Force | Out-Null

if ($Build) {
    Step "2.5/7 先运行根目录 build.ps1 刷新 dist（$Configuration）"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -Configuration $Configuration -SkipMsi
    Assert-ExitCode 'build.ps1'
}

Step "3/7 暂存发布包 dist → installer\.stage（$Configuration）"
if (-not (Test-Path (Join-Path $dist 'LTFC.exe'))) {
    throw ('没有找到发布包：' + $dist + '（先运行根目录 build.ps1，或给本脚本加 -Build）')
}

# dist 根目录：主程序与依赖（.pdb 不进安装包）
Get-ChildItem $dist -File | Where-Object { $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $stage -Force
# 内置资源与内置模块（目录结构与 dist 保持一致，LTFC 依赖这个相对结构找模块）
foreach ($dir in 'assets', 'modules') {
    $source = Join-Path $dist $dir
    if (Test-Path $source) { Copy-Item $source $stage -Recurse -Force }
}
# 视频下载：只有显式要求时才打进安装包（约 800 MB）
if ($IncludeVideoDownload) {
    $video = Join-Path $dist 'VideoDownload'
    if (-not (Test-Path $video)) { throw ('没有找到视频下载目录：' + $video + '（先运行 build.ps1 内置它）') }
    Copy-Item $video $stage -Recurse -Force
    $videoSize = [math]::Round(((Get-ChildItem $video -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Note ('已包含视频下载：VideoDownload（' + $videoSize + ' MB）')
} else {
    Note '未包含视频下载（需要时加 -IncludeVideoDownload）'
}
# AutoNotice 的 command.txt 不进安装目录：Program Files 普通用户不可写，
# 程序会自动在 %APPDATA%\LTFC\AutoNotice\command.txt 生成同款模板。
if (Test-Path (Join-Path $dist 'AutoNotice')) {
    Note '跳过 dist\AutoNotice\command.txt：配置文件由程序在 %APPDATA%\LTFC 下自动生成（可写）'
}
# 许可摘要随程序一起装进安装目录（LTFC.wxs 里按住 .stage\*.txt 打包）
Copy-Item $licenseTxt (Join-Path $stage '许可（摘要）.txt') -Force

$stagedFiles = @(Get-ChildItem $stage -Recurse -File)
$stagedSize = [math]::Round((($stagedFiles | Measure-Object Length -Sum).Sum) / 1MB, 1)
Note ('暂存：' + $stagedFiles.Count + ' 个文件，' + $stagedSize + ' MB')

Step '4/7 暂存内容自检'
$required = @(
    'LTFC.exe',
    'LTFC.Core.dll',
    'LTFC.exe.config',
    'assets\LTFC.ico',
    'assets\赞赏码.jpg',
    'modules\ClassTell\ClassTell.exe',
    'modules\OpenChoose\PPTChooser.exe'
)
$missing = @()
foreach ($item in $required) {
    if (Test-Path (Join-Path $stage $item)) { Note ('必需：' + $item) } else { $missing += $item }
}
if ($missing.Count -gt 0) { throw ('暂存目录缺少必需文件：' + ($missing -join '、') + '（检查 dist 是否完整）') }

# 版本核对：LTFC.exe 的文件版本必须是 1.0.2.x（与 MSI 的 Version 1.0.2 对应）
$fileVersion = (Get-Item (Join-Path $stage 'LTFC.exe')).VersionInfo.FileVersion
Note ('LTFC.exe 文件版本：' + $fileVersion)
if ($fileVersion -notmatch ('^' + [regex]::Escape($Version) + '(\.|$)')) {
    throw ('LTFC.exe 版本（' + $fileVersion + '）与 -Version ' + $Version + ' 不一致：' +
           '请同步 src\LTFC\LTFC.Core\Ui\Theme.cs 里的 VersionText、LTFC.csproj 的 Version 与 LTFC.wxs 的 Package/@Version。')
}

# 只允许这些顶层内容，其余（例如 dist 里出现其它模块）说明构建脚本变了，需要人工确认
$allowed = @('LTFC.exe', 'LTFC.Core.dll', 'LTFC.exe.config', 'assets', 'modules', 'VideoDownload', '许可（摘要）.txt')
$unexpected = @(Get-ChildItem $stage | Where-Object { $allowed -notcontains $_.Name })
if ($unexpected.Count -gt 0) {
    Write-Host ('    注意：暂存目录里有未预期的内容（LTFC.wxs 不会打包它们）：' +
                (($unexpected | Select-Object -ExpandProperty Name) -join '、')) -ForegroundColor Yellow
}

Step '5/7 生成 license.rtf（ASCII + \u 转义，供许可页显示）'
$licenseText = [IO.File]::ReadAllText($licenseTxt, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{\rtf1\ansi\ansicpg936\deff0{\fonttbl{\f0\fswiss\fcharset134 Microsoft YaHei;}}')
[void]$sb.Append("`r`n")
[void]$sb.Append('\viewkind4\uc1\pard\f0\fs20')
[void]$sb.Append("`r`n")
foreach ($ch in $licenseText.ToCharArray()) {
    $code = [int]$ch
    if ($ch -eq "`r") { continue }
    elseif ($ch -eq "`n") { [void]$sb.Append('\par' + "`r`n") }
    elseif ($ch -eq '\') { [void]$sb.Append('\\') }
    elseif ($ch -eq '{') { [void]$sb.Append('\{') }
    elseif ($ch -eq '}') { [void]$sb.Append('\}') }
    elseif ($code -ge 32 -and $code -lt 127) { [void]$sb.Append($ch) }
    else {
        if ($code -gt 32767) { $code -= 65536 }     # RTF 的 \u 只接受有符号 16 位
        [void]$sb.Append('\u' + $code + '?')
    }
}
[void]$sb.Append('}')
[IO.File]::WriteAllText($licenseRtf, $sb.ToString(), [Text.Encoding]::ASCII)
Note ('已生成 installer\license.rtf（' + (Get-Item $licenseRtf).Length + ' 字节）')

Step "6/7 wix build 生成 MSI（$msiName）"
$includeVideo = if ($IncludeVideoDownload) { 'yes' } else { 'no' }
$wixArgs = @('build', '-b', $PSScriptRoot, '-arch', 'x64',
             '-ext', 'WixToolset.UI.wixext', '-ext', 'WixToolset.Util.wixext',
             '-d', ('IncludeVideoDownload=' + $includeVideo),
             '-o', $msi, $wxs)
& $wix @wixArgs '-culture' 'zh-cn' | Out-Host
if (-not (Test-Path $msi)) {
    Write-Host '    中文界面资源尝试失败，改用默认区域重试（Package/@Codepage=936 保证安装包内中文正常）' -ForegroundColor Yellow
    & $wix @wixArgs | Out-Host
}
if (-not (Test-Path $msi)) { throw 'wix build 没有生成 MSI' }

# 新鲜度校验：输出被别的进程占用时，wix 可能“看似成功”却留下上一次的旧文件
$stagedExe = Get-Item (Join-Path $stage 'LTFC.exe')
$builtMsi = Get-Item $msi
if ($builtMsi.LastWriteTime -lt $stagedExe.LastWriteTime) {
    throw ('生成的 MSI 比暂存的 LTFC.exe 还旧，输出文件可能被占用（通常是校验过程留下的 Windows Installer 服务句柄）。' +
           '以管理员身份重启“Windows Installer”服务或重启电脑，或者换一个 -OutputDir 再试。')
}

# 体积校验：暂存文件没被打进去（空包）会小得离谱
$minSizeMB = if ($IncludeVideoDownload) { 100 } else { 1 }
if ($builtMsi.Length -lt ($minSizeMB * 1MB)) {
    throw ('生成的 MSI 只有 ' + [Math]::Round($builtMsi.Length / 1KB) + ' KB，暂存文件很可能没有被打包：' +
           '请核对 LTFC.wxs 里的 .stage\ 路径与暂存目录 ' + $stage + ' 是否一致。')
}

# ---------- 直接读 MSI 表（Windows Installer 自动化接口，不需要装任何东西） ----------
function Get-MsiRows {
    param([string]$MsiPath, [string]$Query)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.OpenDatabase($MsiPath, 0)      # 0 = 只读打开
    $view = $db.OpenView($Query)
    $view.Execute()
    $rows = New-Object System.Collections.ArrayList
    while ($true) {
        $rec = $view.Fetch()
        if ($null -eq $rec) { break }
        $values = New-Object System.Collections.ArrayList
        for ($i = 1; $i -le $rec.FieldCount; $i++) {
            $text = ''
            try { $text = [string]$rec.StringData($i) } catch { $text = '<非文本>' }
            [void]$values.Add($text)
        }
        [void]$rows.Add($values.ToArray())
    }
    $view.Close()
    return ,$rows
}

if (-not $SkipValidate) {
    Step '7/7 校验：ICE 检查 + 读 MSI 表核对内容'

    $report = Join-Path $outDir 'msi-validation.txt'
    & $wix msi validate $msi 2>&1 | Out-File -FilePath $report -Encoding UTF8
    $iceLines = @(Get-Content $report -Encoding UTF8 | Where-Object { $_ -match 'ICE|error|warning|validation' })
    if ($iceLines.Count -gt 0) {
        $iceLines | Select-Object -First 15 | ForEach-Object { Note $_ }
    } else {
        Note 'ICE 校验没有输出（视为通过）'
    }

    try {
        $props = @{}
        foreach ($row in (Get-MsiRows -MsiPath $msi -Query 'SELECT Property, Value FROM Property')) {
            $props[$row[0]] = $row[1]
        }
        Note ('产品：' + $props['ProductName'] + '，版本：' + $props['ProductVersion'] +
              '，厂商：' + $props['Manufacturer'])
        Note ('安装范围：' + $props['ALLUSERS'] + '（1 = 每机器安装到 Program Files）')
        Note ('产品代码：' + $props['ProductCode'])

        # 安装目录结构：只关心 Program Files\LTFC 这一支
        $dirMap = @{}
        foreach ($row in (Get-MsiRows -MsiPath $msi -Query 'SELECT Directory, Directory_Parent, DefaultDir FROM Directory')) {
            $dirMap[$row[0]] = @{ Parent = $row[1]; Name = $row[2] }
        }
        function Resolve-MsiDirPath([string]$id) {
            $parts = New-Object System.Collections.ArrayList
            $current = $id
            $guard = 0
            while ($current -and $dirMap.ContainsKey($current) -and $guard -lt 20) {
                [void]$parts.Insert(0, $dirMap[$current].Name)
                $current = $dirMap[$current].Parent
                $guard++
            }
            return ($parts -join '\')
        }
        $installDirs = @()
        foreach ($id in @($dirMap.Keys)) {
            $path = Resolve-MsiDirPath $id
            if ($path -like 'ProgramFiles64Folder\LTFC*') { $installDirs += $path }
        }
        Note '安装目录结构（与发布包 dist 一致，内置模块靠它定位）：'
        $installDirs | Sort-Object | ForEach-Object { Note ('  · ' + $_) }

        # 文件清单：MSI 里的文件数必须与暂存目录一致
        $msiFiles = @(Get-MsiRows -MsiPath $msi -Query 'SELECT FileName FROM File')
        Note ('打包文件数：MSI ' + $msiFiles.Count + ' 个 / 暂存 ' + $stagedFiles.Count + ' 个')
        if ($msiFiles.Count -ne $stagedFiles.Count) {
            Write-Host '    注意：两者数量不一致，请核对 LTFC.wxs 里的 .stage\ 通配符有没有漏目录' -ForegroundColor Yellow
        }
        $msiFiles | Sort-Object { $_[0] } | ForEach-Object { Note ('  · ' + $_[0]) }

        # 关键的一条：Upgrade 表里必须有旧版独立 ClassTell 的 UpgradeCode
        $upgrade = @(Get-MsiRows -MsiPath $msi -Query 'SELECT UpgradeCode, VersionMin, VersionMax, Attributes, ActionProperty FROM Upgrade')
        $foundLegacy = $false
        foreach ($row in $upgrade) {
            Note ('Upgrade：' + $row[0] + '  版本 ' + $row[1] + '–' + $row[2] +
                  '  属性 ' + $row[4] + '（Attributes=' + $row[3] + '）')
            if ($row[0] -ieq $legacyClassTellUpgradeCode) { $foundLegacy = $true }
        }
        if ($foundLegacy) {
            Write-Host '    已确认：安装 LTFC 时会覆盖安装旧版独立 ClassTell（命中 Upgrade 表，先卸载旧包）' -ForegroundColor Green
        } else {
            throw ('Upgrade 表里没有找到旧 ClassTell 的 UpgradeCode ' + $legacyClassTellUpgradeCode +
                   '：覆盖安装旧 ClassTell 的行为失效，请检查 LTFC.wxs 的 <Upgrade> 记录。')
        }

        # 自启动项清理（卸载时执行）是否在表里
        $regRows = @(Get-MsiRows -MsiPath $msi -Query 'SELECT Registry, Name, Value FROM RemoveRegistry')
        $runCleanup = @($regRows | Where-Object { $_[1] -eq 'LTFC' })
        if ($runCleanup.Count -gt 0) { Note '已包含卸载时的自启动项清理（HKCU\...\Run\LTFC）' }
        else { Write-Host '    注意：没有找到自启动项清理条目（卸载后可能残留 HKCU\...\Run\LTFC）' -ForegroundColor Yellow }
    }
    catch {
        Write-Host ('    读 MSI 表失败（不影响安装包本身）：' + $_.Exception.Message) -ForegroundColor Yellow
    }
    Note ('校验报告：' + $report)
}

$info = Get-Item $msi
Write-Host ''
Write-Host ('OK -> ' + $info.FullName + '  (' + [Math]::Round($info.Length / 1MB, 2) + ' MB)') -ForegroundColor Green
Write-Host ('安装（双击即可，或）：msiexec /i "' + $info.FullName + '"')
Write-Host ('静默安装：msiexec /i "' + $info.FullName + '" /qn')
Write-Host '默认行为：升级/覆盖安装（含自动卸载旧版独立 ClassTell）；卸载：控制面板或开始菜单“卸载 LTFC”'

