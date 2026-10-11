# Scratch: create a deck and run a real slideshow (for monitor integration tests).
param([string]$Action = 'run')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$app = $null
try { $app = [System.Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application') } catch { }
if (-not $app) { $app = New-Object -ComObject PowerPoint.Application }

if ($Action -eq 'run') {
  $pres = $app.Presentations.Add($true)
  $pres.Slides.Add(1, 12) | Out-Null
  $pres.Slides.Add(2, 12) | Out-Null
  $pres.Slides.Item(1).Shapes.AddTextbox(1, 60, 60, 600, 120).TextFrame.TextRange.Text = 'Touch mode monitor test'
  $pres.SaveAs([System.IO.Path]::Combine($env:TEMP, 'touchmode_test.pptx'))
  $pres.SlideShowSettings.ShowType = 1
  $pres.SlideShowSettings.Run()
  Start-Sleep -Milliseconds 1500
  Write-Output ('showWindows=' + $app.SlideShowWindows.Count + ' state=' + $app.SlideShowWindows.Item(1).View.State + ' pres=' + $pres.Name)
} elseif ($Action -eq 'exit') {
  try { $app.SlideShowWindows.Item(1).View.Exit() } catch { Write-Output ('exitErr=' + $_.Exception.Message) }
  Start-Sleep -Milliseconds 800
  foreach ($p in @($app.Presentations)) {
    if ($p.Name -like 'touchmode_test*') { $p.Saved = $true; $p.Close(); Write-Output ('closed ' + $p.Name) }
  }
} else {
  Write-Output ('showWindows=' + $app.SlideShowWindows.Count)
}
Write-Output 'done'
