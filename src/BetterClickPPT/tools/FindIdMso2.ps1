# Round 2: find the Touch/Mouse mode control id (idMso) and check plain toolbar enumeration.
param([int]$MaxScan = 0)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$TouchName = ([string][char]0x89E6) + [char]0x6478 + '/' + [char]0x9F20 + [char]0x6807 + [char]0x6A21 + [char]0x5F0F
$app = $null
try { $app = [System.Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application') } catch { }
if (-not $app) { $app = New-Object -ComObject PowerPoint.Application }
$cb = $app.CommandBars
function Codes($s) { $r = @(); foreach ($ch in $s.ToCharArray()) { $r += ('0x{0:X4}' -f [int]$ch) }; return ($r -join ',') }

Write-Output '--- A candidate ids ---'
$ids = @('Save', 'FromBeginning', 'Undo', 'Redo', 'Repeat', 'CustomizeQAT', 'CustomizeQuickAccessToolbar',
  'TouchMode', 'TouchMouseMode', 'MouseMode', 'TouchModeToggle', 'TouchMouseModeToggle', 'ToggleTouchMode', 'ToggleMouseMode',
  'Touch', 'TouchMouse', 'TouchKeyboardMode', 'TouchOptimizedMode', 'TouchMouseModeButton', 'ModeToggle', 'SwitchMode',
  'InputMode', 'TouchInputMode', 'TouchModeSwitch', 'TouchUIMode', 'OfficeTouchMode', 'TouchModeButton', 'UseTouchMode',
  'TouchOrMouseMode', 'MouseTouchMode', 'TouchMouseInputMode', 'HandwritingMode', 'PenMode', 'StylusMode', 'TouchModeOn',
  'TouchModeOff', 'TouchModeEnabled', 'TouchModeState', 'ToggleTouch', 'TouchModeStatus', 'TouchMouseToggle', 'CmdTouchMode',
  'TouchModeQAT', 'TouchMouseModeQat', 'IsTouchMode', 'MouseModeToggle', 'TouchModeMenu', 'TouchOnlyMode')
$keep = @{}
foreach ($id in $ids) {
  try {
    $l = [string]$cb.GetLabelMso($id)
    $mark = ''
    if ($l -eq $TouchName) { $mark = '   <<< MATCH'; $keep[$id] = $true }
    Write-Output ("  {0,-28} label=[{1}] codes={2}{3}" -f $id, $l, (Codes $l), $mark)
  } catch { }
}

Write-Output '--- B command bars ---'
$n = 0
foreach ($bar in $cb) {
  $n++
  $nm = [string]$bar.Name
  Write-Output ("  bar{0,-4} name=[{1}]" -f $n, $nm)
  if ($nm -match 'QAT|Quick|Ribbon|Status|Toolbar|Bar') {
    $j = 0
    foreach ($ctl in $bar.Controls) {
      $j++
      Write-Output ("      ctl[{0}] caption=[{1}] id={2} tag=[{3}] onAction=[{4}] vis={5}" -f $j, [string]$ctl.Caption, $ctl.Id, [string]$ctl.Tag, [string]$ctl.OnAction, $ctl.Visible)
      if ($j -ge 25) { break }
    }
  }
}

Write-Output '--- B2 CommandBars methods ---'
$cb | Get-Member -MemberType Method | ForEach-Object { Write-Output ('  ' + $_.Name) }

Write-Output '--- C scan Office binaries for TouchMode ---'
if ($MaxScan -le 0) { Write-Output '  (skipped)'; Write-Output 'done'; exit 0 }
$dirs = @('C:\Program Files\Common Files\Microsoft Shared\OFFICE16', 'C:\Program Files\Microsoft Office\Root\Office16')
foreach ($d in $dirs) {
  if (-not (Test-Path $d)) { Write-Output ('  missing ' + $d); continue }
  $files = Get-ChildItem -Path ($d + '\*') -Include '*.dll', '*.exe', '*.mui' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -gt 200000 -and $_.Length -lt 120000000 } | Sort-Object Length -Descending | Select-Object -First $MaxScan
  Write-Output ('  dir=' + $d + ' scanning=' + $files.Count)
  foreach ($f in $files) {
    $hit = $null
    foreach ($pat in @('TouchMode', 'TouchMouseMode')) {
      try {
        $m = Select-String -Path $f.FullName -Pattern $pat -SimpleMatch -Encoding unicode -List -ErrorAction Stop
        if ($m) { $hit = $pat + ' @' + $m.LineNumber }
      } catch { }
      if ($hit) { break }
    }
    if ($hit) { Write-Output ('    HIT ' + $f.Name + ' size=' + $f.Length + ' ' + $hit) }
  }
}
Write-Output 'done'
