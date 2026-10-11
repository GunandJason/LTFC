# make-icon.ps1
# 生成 OpenChoose 应用图标：一个鼠标指针（箭头光标），写入 src\PPTChooser\Resources\app.ico
#
#   * 每个尺寸单独矢量绘制 + 抗锯齿，高分屏、任务栏、托盘、文件图标都清晰
#   * ICO 采用 Windows 推荐结构：
#       16 / 24 / 32 / 48 / 64  -> BMP(DIB) 帧（.NET 的 System.Drawing.Icon 能正确读取）
#       128 / 256               -> PNG 压缩帧（供资源管理器大图标使用，体积小）
#
# 用法： powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\PPTChooser\Resources\app.ico')
)

Add-Type -AssemblyName System.Drawing

$dibSizes = @(16, 24, 32, 48, 64)
$pngSizes = @(128, 256)

# 经典 Windows 箭头光标轮廓（坐标基于 24 x 24 设计框）
$cursorShape = @(
    @(5.0, 2.0),    # 箭头尖端（左上）
    @(5.0, 20.0),   # 左边线向下
    @(9.0, 16.0),   # 底部凹口
    @(12.0, 23.0),  # 尾巴尖端
    @(15.0, 22.0),  # 尾巴右下
    @(12.0, 15.0),  # 尾部回到箭身
    @(18.0, 15.0)   # 箭身右下角
)

# 把光标轮廓按目标尺寸（含偏移，用于投影）写入传入的列表
function Fill-CursorPoints(
    [System.Collections.Generic.List[System.Drawing.PointF]]$list,
    [int]$size,
    [double]$offset) {

    $padding = [Math]::Max(1.0, $size * 0.06)
    $scale = ($size - 2.0 * $padding) / 24.0
    $list.Clear()

    foreach ($shapePoint in $cursorShape) {
        $x = [single]($padding + $shapePoint[0] * $scale + $offset)
        $y = [single]($padding + $shapePoint[1] * $scale + $offset)
        $list.Add((New-Object System.Drawing.PointF($x, $y)))
    }
}

# 绘制单个尺寸的光标位图
function New-CursorBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $points = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    Fill-CursorPoints $points $size 0.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon($points.ToArray())

    # 大尺寸加柔和投影，小尺寸保持干净
    if ($size -ge 24) {
        $offset = [Math]::Max(1.0, $size * 0.045)
        $shadowPoints = New-Object System.Collections.Generic.List[System.Drawing.PointF]
        Fill-CursorPoints $shadowPoints $size $offset
        $shadowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $shadowPath.AddPolygon($shadowPoints.ToArray())
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(64, 0, 0, 0))
        $graphics.FillPath($shadowBrush, $shadowPath)
        $shadowBrush.Dispose()
        $shadowPath.Dispose()
    }

    # 白色填充 + 黑色描边：与系统光标观感一致，深浅背景都清晰
    $graphics.FillPath([System.Drawing.Brushes]::White, $path)
    $penWidth = [single][Math]::Min(4.0, [Math]::Max(1.0, $size / 20.0))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, $penWidth)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($pen, $path)

    $pen.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    return $bitmap
}

# 把位图以 ICO 的 BMP(DIB) 帧格式写入目标流（BITMAPINFOHEADER + BGRA 像素 + AND 掩码）
function Add-DibFrame([System.IO.Stream]$target, [System.Drawing.Bitmap]$bitmap, [int]$size) {
    $xorSize = $size * $size * 4
    # AND 掩码每行 1bpp，且按 4 字节对齐（注意：必须用整数运算，PowerShell 的 [int] 会四舍五入）
    $maskRowBytes = [int]([Math]::Ceiling($size / 32.0) * 4)
    $maskSize = $maskRowBytes * $size

    $writer = New-Object System.IO.BinaryWriter($target)

    # BITMAPINFOHEADER
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$size)
    $writer.Write([Int32]($size * 2))   # 高度 = XOR + AND 两张图
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)            # BI_RGB
    $writer.Write([UInt32]($xorSize + $maskSize))
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)

    # 像素数据：自下而上，每像素 BGRA
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $row = New-Object byte[] $stride
        for ($y = $size - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]::Add($data.Scan0, $y * $stride), $row, 0, $stride)
            $writer.Write($row, 0, $size * 4)
        }
    } finally {
        $bitmap.UnlockBits($data)
    }

    # AND 掩码：全 0（透明度由 BGRA 的 alpha 通道决定）
    $maskRow = New-Object byte[] $maskRowBytes
    for ($y = 0; $y -lt $size; $y++) {
        $writer.Write($maskRow, 0, $maskRowBytes)
    }

    $writer.Flush()
}

# 把位图以 PNG 帧格式写入目标流
function Add-PngFrame([System.IO.Stream]$target, [System.Drawing.Bitmap]$bitmap) {
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
}

# 逐尺寸生成帧数据（先写入独立的负载流，避免 PowerShell 展开 byte[] 带来的问题）
$payload = New-Object System.IO.MemoryStream
$entries = @()

foreach ($size in $dibSizes) {
    $bitmap = New-CursorBitmap $size
    $start = $payload.Position
    Add-DibFrame $payload $bitmap $size
    $entries += , @{ Size = $size; Offset = $start; Length = ($payload.Position - $start) }
    $bitmap.Dispose()
}

foreach ($size in $pngSizes) {
    $bitmap = New-CursorBitmap $size
    $start = $payload.Position
    Add-PngFrame $payload $bitmap
    $entries += , @{ Size = $size; Offset = $start; Length = ($payload.Position - $start) }
    $bitmap.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

# ICO 容器：6 字节头 + 每帧 16 字节目录项 + 帧数据
$fileStream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)                 # reserved
$writer.Write([UInt16]1)                 # type = icon
$writer.Write([UInt16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($entry in $entries) {
    $dimension = 0
    if ($entry.Size -lt 256) { $dimension = [Byte]$entry.Size }   # 256 记作 0
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)               # 调色板数量
    $writer.Write([Byte]0)               # reserved
    $writer.Write([UInt16]1)             # color planes
    $writer.Write([UInt16]32)            # bits per pixel
    $writer.Write([UInt32]$entry.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Length
}

$writer.Flush()
$payload.Position = 0
$payload.CopyTo($fileStream)
$writer.Dispose()
$fileStream.Dispose()
$payload.Dispose()

Write-Output ('icon written: ' + (Resolve-Path $OutputPath) + ' (' + (Get-Item $OutputPath).Length + ' bytes; DIB ' +
    ($dibSizes -join '/') + ' + PNG ' + ($pngSizes -join '/') + ')')

