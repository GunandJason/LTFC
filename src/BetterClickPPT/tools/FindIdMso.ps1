# Discover the real Office ribbon control id (idMso) of the "Touch/Mouse Mode" toggle.
# Sources: (A) validate Application.CommandBars.GetXxxMso, (B) Office UI registry blobs, (C) PowerPoint binaries.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$app = $null
try { $app = [System.Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application') } catch { }
if (-not $app) { $app = New-Object -ComObject PowerPoint.Application }
$cb = $app.CommandBars

Write-Output '--- A validate mechanism with known ids ---'
foreach ($id in @('Bold', 'Copy', 'Paste', 'Undo')) {
  foreach ($m in @('GetLabelMso', 'GetEnabledMso')) {
    try { Write-Output ("  $m($id) = [" + [string]$cb.$m($id) + ']') } catch { Write-Output ("  $m($id) ERR " + $_.Exception.Message) }
  }
}

Write-Output '--- B registry Office UI blobs ---'
$pats = @('Touch', 'Mouse')
$hits = 0
function ScanKey($k, [int]$depth) {
  if ($depth -gt 5) { return }
  try { $vns = $k.GetValueNames() } catch { return }
  foreach ($vn in $vns) {
    $v = $null
    try { $v = $k.GetValue($vn) } catch { continue }
    if ($v -is [byte[]]) {
      $s = [System.Text.Encoding]::Unicode.GetString($v)
      foreach ($p in $script:pats) {
        if ($s.Contains($p)) {
          $script:hits++
          Write-Output ('  BYTES ' + $k.Name + ' :: ' + $vn + ' :: ' + $s.Substring(0, [Math]::Min(700, $s.Length)))
          break
        }
      }
    } elseif ($v -is [string] -and ($v -match 'Touch|Mouse')) {
      $script:hits++
      Write-Output ('  STR ' + $k.Name + ' :: ' + $vn + ' = ' + $v)
    }
  }
  try { $subs = $k.GetSubKeyNames() } catch { return }
  foreach ($sn in $subs) {
    try { ScanKey $k.OpenSubKey($sn) ($depth + 1) } catch { }
  }
}
foreach ($root in @('Software\Microsoft\Office\16.0\Common\Toolbars', 'Software\Microsoft\Office\16.0\PowerPoint\Toolbars', 'Software\Microsoft\Office\16.0\Common\General')) {
  try { ScanKey ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($root)) 0 } catch { Write-Output ('  skip ' + $root) }
}
Write-Output ('  registryHits=' + $hits)

Write-Output '--- C binaries: extract idMso list ---'
$exe = $null
foreach ($k in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\POWERPNT.EXE', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\POWERPNT.EXE')) {
  try { $v = (Get-ItemProperty $k -ErrorAction Stop).'(default)'; if ($v) { $exe = $v } } catch { }
}
if (-not $exe) {
  $c = Get-ChildItem 'C:\Program Files\Microsoft Office' -Recurse -Filter 'POWERPNT.EXE' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($c) { $exe = $c.FullName }
}
if (-not $exe) {
  $c = Get-ChildItem 'C:\Program Files (x86)\Microsoft Office' -Recurse -Filter 'POWERPNT.EXE' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($c) { $exe = $c.FullName }
}
Write-Output ('  exe=' + $exe)
if (-not $exe) { Write-Output 'NO POWERPNT.EXE'; exit 5 }
$dir = Split-Path $exe
$files = @($exe)
foreach ($n in @('Mso.dll', 'msores.dll', 'Mso30win32client.dll')) {
  $f = Join-Path $dir $n
  if (Test-Path $f) { $files += $f }
}
$rx = [regex]'idMso\s*=\s*"([A-Za-z0-9_]+)"'
foreach ($f in $files) {
  $bytes = [System.IO.File]::ReadAllBytes($f)
  Write-Output ('  file=' + (Split-Path $f -Leaf) + ' size=' + $bytes.Length)
  foreach ($enc in @('Unicode', 'ASCII')) {
    $s = [System.Text.Encoding]::$enc.GetString($bytes)
    $ids = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in $rx.Matches($s)) { [void]$ids.Add($m.Groups[1].Value) }
    Write-Output ('    enc=' + $enc + ' idMsoTotal=' + $ids.Count)
    $sel = @($ids | Where-Object { $_ -match 'Touch|Mouse|Mode' } | Sort-Object)
    Write-Output ('    modeIds=' + $sel.Count)
    foreach ($i in $sel) { Write-Output ('      ' + $i) }
    if ($ids.Contains('Save') -or $ids.Contains('Bold')) { Write-Output '      (sanity: Save/Bold present)' }
    foreach ($pat in @('TouchMode', 'TouchMouseMode', 'MouseMode')) {
      $cnt = 0
      $j = $s.IndexOf($pat)
      while ($j -ge 0) { $cnt++; $j = $s.IndexOf($pat, $j + 1) }
      if ($cnt -gt 0) {
        $st = [Math]::Max(0, $s.IndexOf($pat) - 200)
        Write-Output ('    pat=' + $pat + ' count=' + $cnt + ' ctx=' + $s.Substring($st, [Math]::Min(400, $s.Length - $st)).Replace("`r", ' ').Replace("`n", ' '))
      }
    }
  }
}
Write-Output 'done'
