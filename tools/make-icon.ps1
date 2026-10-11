# make-icon.ps1 —— 由品牌图片生成多尺寸 ICO（不重新设计图标，只做格式转换与缩放）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -Source <图片> -Output <ico>
param(
    [string]$Source = 'C:\Users\Admin\Desktop\logo.png',
    [string]$Output = '',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $root 'assets\LTFC.ico' }

if (-not (Test-Path $Source)) { throw "找不到源图片：$Source" }

$dir = Split-Path -Parent $Output
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$sourceImage = [System.Drawing.Image]::FromFile($Source)

# 逐尺寸等比缩放到方形画布（保持透明背景）
$pngs = @()
foreach ($size in ($Sizes | Sort-Object)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = [Math]::Min($size / $sourceImage.Width, $size / $sourceImage.Height)
    $w = [int][Math]::Round($sourceImage.Width * $scale)
    $h = [int][Math]::Round($sourceImage.Height * $scale)
    $x = [int](($size - $w) / 2)
    $y = [int](($size - $h) / 2)
    $graphics.DrawImage($sourceImage, $x, $y, $w, $h)
    $graphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    $bitmap.Dispose()
}
$sourceImage.Dispose()

# 写 ICO（Vista 及以上支持 PNG 负载）
$fileStream = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)                 # reserved
$writer.Write([UInt16]1)                 # type = icon
$writer.Write([UInt16]$pngs.Count)       # count

$offset = 6 + 16 * $pngs.Count
foreach ($entry in $pngs) {
    $size = [int]$entry.Size
    $bytes = $entry.Bytes
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))   # width
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))   # height
    $writer.Write([Byte]0)               # colors
    $writer.Write([Byte]0)               # reserved
    $writer.Write([UInt16]1)             # planes
    $writer.Write([UInt16]32)            # bit count
    $writer.Write([UInt32]$bytes.Length) # size
    $writer.Write([UInt32]$offset)       # offset
    $offset += $bytes.Length
}

foreach ($entry in $pngs) { $writer.Write($entry.Bytes) }
$writer.Flush()
$writer.Close()
$fileStream.Close()

$info = Get-Item $Output
Write-Host ('已生成图标：' + $info.FullName + '（' + $info.Length + ' 字节，' + $pngs.Count + ' 个尺寸）')
