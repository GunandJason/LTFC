# make-banner.ps1 —— 生成安装向导用的图片（installer\WixUIBanner.bmp 493x58、WixUIDialogBmp 493x312）
# 图案取自 app.ico（鼠标指针），配渐变背景与产品名。
param(
    [string]$Icon = (Join-Path $PSScriptRoot '..\src\PPTChooser\Resources\app.ico'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\installer')
)

Add-Type -AssemblyName System.Drawing

# 从 ICO 里取出 PNG 帧（128 或 256）
function Get-IconFrame([int]$size) {
    $bytes = [System.IO.File]::ReadAllBytes($Icon)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + 16 * $i
        $frameSize = [int]$bytes[$entry]
        if ($frameSize -eq 0) { $frameSize = 256 }
        if ($frameSize -ne $size) { continue }

        $length = [int][BitConverter]::ToUInt32($bytes, $entry + 8)
        $offset = [int][BitConverter]::ToUInt32($bytes, $entry + 12)

        # 128/256 是 PNG 帧，直接从容器里取出解码；16~64 是 DIB 帧，交给 System.Drawing.Icon
        $isPng = ($length -gt 8 -and $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50)
        if ($isPng) {
            $slice = New-Object byte[] $length
            [Array]::Copy($bytes, $offset, $slice, 0, $length)
            $stream = New-Object System.IO.MemoryStream -ArgumentList @(, $slice)
            return (New-Object System.Drawing.Bitmap([System.Drawing.Image]::FromStream($stream)))
        }

        $icon = New-Object System.Drawing.Icon($Icon, (New-Object System.Drawing.Size($size, $size)))
        $bitmap = $icon.ToBitmap()
        $icon.Dispose()
        return $bitmap
    }
    throw "图标中没有 $size 像素的帧"
}

function Add-Background([System.Drawing.Graphics]$graphics, [int]$width, [int]$height) {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 32, 94, 196),
        [System.Drawing.Color]::FromArgb(255, 10, 40, 110),
        20.0)
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()
}

$icon128 = Get-IconFrame 128
$icon48 = Get-IconFrame 48
$fontFamily = 'Microsoft YaHei UI'
try { $fontFamily = (New-Object System.Drawing.FontFamily('Microsoft YaHei UI')).Name } catch { $fontFamily = 'Microsoft Sans Serif' }

# 左侧大图 493 x 312
$dialog = New-Object System.Drawing.Bitmap(493, 312)
$dialogGraphics = [System.Drawing.Graphics]::FromImage($dialog)
$dialogGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$dialogGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$dialogGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
Add-Background $dialogGraphics 493 312
$dialogGraphics.DrawImage($icon128, 190, 40, 112, 112)
$titleFont = New-Object System.Drawing.Font($fontFamily, 20, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$subFont = New-Object System.Drawing.Font($fontFamily, 14, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$dialogGraphics.DrawString('OpenChoose', $titleFont, [System.Drawing.Brushes]::White,
    (New-Object System.Drawing.RectangleF(0, 172, 493, 34)), $format)
$dialogGraphics.DrawString('PPT 打开方式选择器', $subFont,
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210, 255, 255, 255))),
    (New-Object System.Drawing.RectangleF(0, 208, 493, 24)), $format)
$dialogGraphics.DrawString('双击 PPT 时自行选择打开程序', $subFont,
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(160, 255, 255, 255))),
    (New-Object System.Drawing.RectangleF(0, 238, 493, 24)), $format)
$dialogGraphics.Dispose()
$dialog.Save((Join-Path $OutDir 'WixUIDialogBmp.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$dialog.Dispose()

# 顶部横条 493 x 58
$banner = New-Object System.Drawing.Bitmap(493, 58)
$bannerGraphics = [System.Drawing.Graphics]::FromImage($banner)
$bannerGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$bannerGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$bannerGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
Add-Background $bannerGraphics 493 58
$bannerGraphics.DrawImage($icon48, 10, 5, 48, 48)
$bannerFont = New-Object System.Drawing.Font($fontFamily, 16, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$bannerSubFont = New-Object System.Drawing.Font($fontFamily, 12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$bannerGraphics.DrawString('OpenChoose 安装程序', $bannerFont, [System.Drawing.Brushes]::White, 68, 8)
$bannerGraphics.DrawString('PPT 打开方式选择器', $bannerSubFont,
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 255, 255, 255))), 69, 30)
$bannerGraphics.Dispose()
$banner.Save((Join-Path $OutDir 'WixUIBannerBmp.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$banner.Dispose()

$icon128.Dispose()
$icon48.Dispose()
Write-Output ('banners written to ' + (Resolve-Path $OutDir))
