#requires -Version 5.1
<#
    生成应用图标 assets\BetterClickPPT.ico（16/32/48/256 四种尺寸）。
    图标为程序化绘制，仓库中不保存二进制图片资源。

    用法： powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\BetterClickPPT.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-AppBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # 圆角背景 + 蓝色渐变
        $radius = [float]($Size * 0.22)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($Size - $d, 0, $d, $d, 270, 90)
        $path.AddArc($Size - $d, $Size - $d, $d, $d, 0, 90)
        $path.AddArc(0, $Size - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.Point(0, 0)),
            (New-Object System.Drawing.Point($Size, $Size)),
            [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
            [System.Drawing.Color]::FromArgb(255, 29, 78, 216))
        $graphics.FillPath($brush, $path)
        $brush.Dispose()
        $path.Dispose()

        # 手指轻触的同心圆（波纹）
        $penWidth = [float]([Math]::Max(1.2, $Size * 0.075))
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 255, 255, 255), $penWidth)
        $ringSize = [float]($Size * 0.30)
        $ringX = [float]($Size * 0.10)
        $ringY = [float](($Size - $ringSize) / 2)
        $graphics.DrawEllipse($pen, $ringX, $ringY, $ringSize, $ringSize)
        $pen.Dispose()

        # 白色“下一页”三角
        $triangle = @(
            (New-Object System.Drawing.PointF([float]($Size * 0.46), [float]($Size * 0.27))),
            (New-Object System.Drawing.PointF([float]($Size * 0.46), [float]($Size * 0.73))),
            (New-Object System.Drawing.PointF([float]($Size * 0.78), [float]($Size * 0.50)))
        )
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $graphics.FillPolygon($white, $triangle)
        $white.Dispose()

        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function Get-BmpIconBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # BITMAPINFOHEADER（高度为 2 倍，含 AND 掩码）
    $writer.Write([uint32]40)
    $writer.Write([int32]$width)
    $writer.Write([int32]($height * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32]($width * $height * 4))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)

    # XOR 数据：自下而上、BGRA
    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $color = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$color.B)
            $writer.Write([byte]$color.G)
            $writer.Write([byte]$color.R)
            $writer.Write([byte]$color.A)
        }
    }

    # AND 掩码：每行按 4 字节对齐
    $maskRowBytes = [int]([Math]::Ceiling($width / 32.0) * 4)
    for ($y = $height - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskRowBytes
        for ($x = 0; $x -lt $width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -lt 128) {
                $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
        $writer.Write($row)
    }

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return $bytes
}

function Get-PngIconBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return $bytes
}

$sizes = @(16, 32, 48, 256)
$images = @()

foreach ($size in $sizes) {
    $bitmap = New-AppBitmap -Size $size
    try {
        # 256 尺寸使用 PNG 压缩（Vista+ 支持），其余使用 BMP
        $bytes = if ($size -ge 256) { Get-PngIconBytes -Bitmap $bitmap } else { Get-BmpIconBytes -Bitmap $bitmap }
        if ($null -eq $bytes) { throw "生成 $size x $size 图标数据失败" }
        $images += [pscustomobject]@{ Size = $size; Bytes = [byte[]]$bytes }
    }
    finally {
        $bitmap.Dispose()
    }
}

foreach ($image in $images) {
    Write-Host ("  - {0}x{0} : {1} 字节数据" -f $image.Size, $image.Bytes.Length)
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$fileStream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)
try {
    # ICONDIR
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    # ICONDIRENTRY（每项 16 字节）
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

$info = Get-Item $OutputPath
Write-Host ("已生成图标：{0} （{1} 字节，包含 {2} 种尺寸）" -f $info.FullName, $info.Length, $images.Count)
