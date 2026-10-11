# check-state.ps1 —— 开发期注册表/窗口状态检查（输出到 dist\state-check.txt）
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\PPTChooser\bin\Release\PPTChooser.exe'
$out = Join-Path $root 'dist\state-check.txt'
$lines = New-Object System.Collections.Generic.List[string]

function Add-Line([string]$text) { $script:lines.Add($text) }

function Run([string[]]$arguments) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru
    return $p.ExitCode
}

function ReadDefault([string]$subKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return '(key missing)' }
    $value = $key.GetValue('')
    $key.Close()
    if ($null -eq $value) { return '(default not set)' }
    return [string]$value
}

function ReadValue([string]$subKey, [string]$name) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $false)
    if ($null -eq $key) { return '(key missing)' }
    $value = $key.GetValue($name)
    $key.Close()
    if ($null -eq $value) { return '(value missing)' }
    return [string]$value
}

$testFile = Join-Path $root 'dist\test-smoke\my demo 示例.pptx'
New-Item -ItemType Directory -Path (Split-Path -Parent $testFile) -Force | Out-Null
Set-Content -Path $testFile -Value 'smoke' -Encoding UTF8

Add-Line '--- register ---'
Add-Line ('exit=' + (Run @('--register', '--silent')))
Add-Line ('ProgID  command  = ' + (ReadDefault 'Software\Classes\OpenChoose.PPT\shell\open\command'))
Add-Line ('ProgID  caption  = ' + (ReadDefault 'Software\Classes\OpenChoose.PPT'))
Add-Line ('.pptx OpenWithProgids(OpenChoose.PPT) = ' + (ReadValue 'Software\Classes\.pptx\OpenWithProgids' 'OpenChoose.PPT'))
Add-Line ('.pptx OpenWithList(PPTChooser.exe)    = ' + (ReadValue 'Software\Classes\.pptx\OpenWithList' 'PPTChooser.exe'))
Add-Line ('Applications command = ' + (ReadDefault 'Software\Classes\Applications\PPTChooser.exe\shell\open\command'))
Add-Line ('RegisteredApplications = ' + (ReadValue 'Software\RegisteredApplications' 'OpenChoose'))
Add-Line ('右键菜单命令 = ' + (ReadDefault 'Software\Classes\.pptx\shell\OpenChoose\command'))

Add-Line '--- setdefault ---'
Add-Line ('exit=' + (Run @('--setdefault', '--silent')))
Add-Line ('.pptx 默认 ProgID = ' + (ReadDefault 'Software\Classes\.pptx'))

Add-Line '--- 弹出选择窗口 ---'
$chooser = Start-Process -FilePath $exe -ArgumentList @($testFile) -PassThru
Start-Sleep -Seconds 3
$chooser.Refresh()
Add-Line ('进程存活 = ' + (-not $chooser.HasExited))
Add-Line ('主窗口句柄 = ' + $chooser.MainWindowHandle)
Add-Line ('主窗口标题 = [' + $chooser.MainWindowTitle + ']')
if (-not $chooser.HasExited) { Stop-Process -Id $chooser.Id -Force }
Start-Sleep -Seconds 1

Add-Line '--- unregister ---'
Add-Line ('exit=' + (Run @('--unregister', '--silent')))
Add-Line ('.pptx 默认 ProgID = ' + (ReadDefault 'Software\Classes\.pptx'))
Add-Line ('ProgID 键存在 = ' + ($null -ne [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\OpenChoose.PPT', $false)))

$lines | Set-Content -Path $out -Encoding UTF8
Write-Host ('written: ' + $out)
