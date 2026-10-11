# make-license-rtf.ps1 —— 生成安装向导的许可协议文本（installer\License.rtf）
# 中文以 RTF 的 \uN 转义写入，避免编码问题。
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\installer\License.rtf')
)

$paragraphs = @(
    'OpenChoose（PPT 打开方式选择器） - 使用许可与说明',
    '',
    '1. 本程序是免费软件，用于在双击 .ppt / .pptx 文件时，让您自行选择用 PowerPoint、WPS Office、LibreOffice Impress 或其它程序打开该文件。',
    '',
    '2. 所有设置（文件关联、开机自启动、所记住的选择）都只写入当前用户的环境，不修改系统其它用户的数据，也不需要管理员权限：',
    '   - 程序文件安装到 %LOCALAPPDATA%\OpenChoose；',
    '   - 文件关联写入 HKCU\Software\Classes；',
    '   - 开机自启动写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。',
    '',
    '3. 卸载时上述内容会一并清理；您在 %APPDATA%\OpenChoose 下的个人配置（settings.ini）会被保留，需要时可自行删除。',
    '',
    '4. 本程序按“现状”提供，不附带任何明示或暗示的担保。使用前请自行确认其行为符合您的需要。',
    '',
    '点击“我接受许可协议中的条款”并继续，即表示您已阅读并同意以上说明。'
)

function ConvertTo-RtfEscapes([string]$text) {
    $builder = New-Object System.Text.StringBuilder
    foreach ($character in $text.ToCharArray()) {
        $code = [int][char]$character
        if ($character -eq '\') { $builder.Append('\\') | Out-Null }
        elseif ($character -eq '{') { $builder.Append('\{') | Out-Null }
        elseif ($character -eq '}') { $builder.Append('\}') | Out-Null }
        elseif ($code -ge 32 -and $code -le 126) { $builder.Append($character) | Out-Null }
        else {
            $signed = $code
            if ($signed -gt 32767) { $signed = $signed - 65536 }
            $builder.Append('\u' + $signed + '?') | Out-Null
        }
    }
    return $builder.ToString()
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('{\rtf1\ansi\ansicpg936\deff0{\fonttbl{\f0\fswiss\fcharset134 Microsoft YaHei;}}')
$lines.Add('\viewkind4\uc1\pard\f0\fs20')
foreach ($paragraph in $paragraphs) {
    $lines.Add((ConvertTo-RtfEscapes $paragraph) + '\par')
}
$lines.Add('}')

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
[System.IO.File]::WriteAllText($OutputPath, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Output ('license written: ' + (Resolve-Path $OutputPath) + ' (' + (Get-Item $OutputPath).Length + ' bytes)')
