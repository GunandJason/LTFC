# msi-wordcount.ps1 —— 打印 MSI 的平台/字计数（字计数 bit3 = 需要管理员权限）
param(
    [Parameter(Mandatory = $true)][string]$Msi
)

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Msi, 0))
$summary = $database.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $database, @(0))

$template = [string]$summary.GetType().InvokeMember('Property', 'GetProperty', $null, $summary, @(7))
$wordCount = [int]$summary.GetType().InvokeMember('Property', 'GetProperty', $null, $summary, @(15))

# 位含义（Windows Installer 摘要信息 Word Count）：
#   bit0(1) 短文件名 ; bit1(2) 压缩 cab ; bit2(4) 管理安装镜像 ; bit3(8) 每用户/LUA 安装（无需管理员）
# 10 = 2 + 8 -> 压缩包 + 支持每用户安装，正是我们要的（安装日志里会写
# "MSI_LUA: Package is marked as LUA installation capable with no elevation required"）。
$luaCapable = ($wordCount -band 8) -ne 0

# 顺便列出 ALLUSERS / MSIINSTALLPERUSER
function Get-Prop([string]$name) {
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database,
        @("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$name'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($null -eq $record) { return '(未定义)' }
    return [string]$record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
}

$name = Split-Path -Leaf $Msi
$allUsers = Get-Prop 'ALLUSERS'
$perUser = Get-Prop 'MSIINSTALLPERUSER'
Write-Output ("{0,-20} template={1,-14} wordCount={2,-4} 每用户/LUA={3,-5} ALLUSERS={4} MSIINSTALLPERUSER={5}" -f
    $name, $template, $wordCount, $luaCapable, $allUsers, $perUser)
