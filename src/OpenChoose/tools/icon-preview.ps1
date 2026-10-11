# icon-preview.ps1 —— 把 app.ico 的各尺寸帧导出为 PNG，便于肉眼检查
#   dist\icon-256.png      256 像素帧
#   dist\icon-zoom-32.png  32 像素帧放大 8 倍（检查小尺寸清晰度）
#   dist\icon-strip.png    16/24/32/48/64 原尺寸排列（上排深色背景、下排浅色背景）
param(
    [string]$Icon = (Join-Path $PSScriptRoot '..\src\PPTChooser\Resources\app.ico'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist')
)

Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# 读取 ICO 帧：DIB 帧交给 System.Drawing.Icon；PNG 帧直接从容器里取出再用 GDI+ 解码
# （.NET Framework 的 Icon 类不能解码 128/256 的 PNG 帧，但 Windows 资源管理器可以）
function Read-IconFrame([int]$size) {
    $bytes = [System.IO.File]::ReadAllBytes($Icon)
    $count = [BitConverter]::ToUInt16($bytes, 4)

    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + 16 * $i
        $frameSize = [int]$bytes[$entry]
        if ($frameSize -eq 0) { $frameSize = 256 }
        if ($frameSize -ne $size) { continue }

        $length = [int][BitConverter]::ToUInt32($bytes, $entry + 8)
        $offset = [int][BitConverter]::ToUInt32($bytes, $entry + 12)

        $isPng = ($length -gt 8 -and $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50)
        if ($isPng) {
            $slice = New-Object byte[] $length
            [Array]::Copy($bytes, $offset, $slice, 0, $length)
            $stream = New-Object System.IO.MemoryStream -ArgumentList @(, $slice)
            $image = [System.Drawing.Image]::FromStream($stream)
            return (New-Object System.Drawing.Bitmap($image))
        }

        $icon = New-Object System.Drawing.Icon($Icon, (New-Object System.Drawing.Size($size, $size)))
        $bitmap = $icon.ToBitmap()
        $icon.Dispose()
        return $bitmap
    }

    throw "图标中没有 $size 像素的帧"
}

$frame256 = Read-IconFrame 256
$frame256.Save((Join-Path $OutDir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$frame32 = Read-IconFrame 32
$zoom = New-Object System.Drawing.Bitmap(256, 256)
$zoomGraphics = [System.Drawing.Graphics]::FromImage($zoom)
$zoomGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$zoomGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$zoomGraphics.DrawImage($frame32, (New-Object System.Drawing.Rectangle(0, 0, 256, 256)))
$zoomGraphics.Dispose()
$zoom.Save((Join-Path $OutDir 'icon-zoom-32.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$stripSizes = @(16, 24, 32, 48, 64)
$rowHeight = 80
$stripHeight = $rowHeight * 2
$width = 32
foreach ($size in $stripSizes) { $width += $size + 16 }
$strip = New-Object System.Drawing.Bitmap($width, $stripHeight)
$stripGraphics = [System.Drawing.Graphics]::FromImage($strip)
$stripGraphics.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
$stripGraphics.FillRectangle([System.Drawing.Brushes]::White, 0, $rowHeight, $width, $rowHeight)
$stripGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$x = 16
foreach ($size in $stripSizes) {
    $frame = Read-IconFrame $size
    $stripGraphics.DrawImage($frame, $x, 8 + (64 - $size) / 2, $size, $size)
    $stripGraphics.DrawImage($frame, $x, $rowHeight + 8 + (64 - $size) / 2, $size, $size)
    $frame.Dispose()
    $x += $size + 16
}

$stripGraphics.Dispose()
$strip.Save((Join-Path $OutDir 'icon-strip.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$strip.Dispose()
$zoom.Dispose()
$frame32.Dispose()
$frame256.Dispose()

Write-Output ('preview written to ' + (Resolve-Path $OutDir))
