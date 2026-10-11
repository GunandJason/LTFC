# shoot.ps1 - capture a window screenshot by process name (for UI verification)
#
# Usage: powershell -ExecutionPolicy Bypass -File tools\shoot.ps1 -ProcessName LTFC -Out shot.png
param(
    [string]$ProcessName = 'LTFC',
    [string]$Out = '',
    [int]$WaitMs = 2500,
    [switch]$UsePrintWindow
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinCap {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

# 必须让本进程 DPI 感知，否则 GetWindowRect 返回虚拟化坐标，截图会被裁切/拉伸
[WinCap]::SetProcessDPIAware() | Out-Null

if ([string]::IsNullOrEmpty($Out)) { $Out = Join-Path $env:TEMP 'window-shot.png' }

Start-Sleep -Milliseconds $WaitMs

$proc = Get-Process -Name $ProcessName -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $proc) { throw "no visible window found for process '$ProcessName'" }

$handle = $proc.MainWindowHandle
if ([WinCap]::IsIconic($handle)) { [WinCap]::ShowWindow($handle, 9) | Out-Null; Start-Sleep -Milliseconds 600 }
[WinCap]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object WinCap+RECT
[WinCap]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "invalid window size ${width}x${height}" }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

if ($UsePrintWindow) {
    $hdc = $graphics.GetHdc()
    [WinCap]::PrintWindow($handle, $hdc, 2) | Out-Null
    $graphics.ReleaseHdc($hdc)
}
else {
    # 直接抓取屏幕上的窗口区域：与用户实际看到的画面一致
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
}

$graphics.Dispose()
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host ("saved: " + $Out + " (" + $width + "x" + $height + ")")
