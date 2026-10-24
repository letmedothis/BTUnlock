# BTUnlock 图标生成脚本：蓝色圆角方块 + 白色挂锁
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-icons.ps1
# 产物：
#   src/BTUnlock.Desktop/app.ico                          PC exe/窗口/托盘
#   src/BTUnlock.Android/Resources/mipmap-*/ic_launcher.png           各密度启动图标
#   src/BTUnlock.Android/Resources/mipmap-*/ic_launcher_foreground.png 自适应前景层
# 通知图标是矢量 XML（Resources/drawable/ic_stat_advertise.xml），不在本脚本范围。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$desktopDir = Join-Path $root 'src/BTUnlock.Desktop'
$androidRes = Join-Path $root 'src/BTUnlock.Android/Resources'

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LockBitmap([int]$size, [double]$contentScale, [bool]$withBackground) {
    $s = $size
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    if ($withBackground) {
        $pad = [float]($s * 0.02)
        $path = New-RoundedRectPath $pad $pad ([float]($s - 2 * $pad)) ([float]($s - 2 * $pad)) ([float]($s * 0.22))
        $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
            [System.Drawing.Color]::FromArgb(255, 59, 130, 246),   # #3B82F6
            [System.Drawing.Color]::FromArgb(255, 29, 78, 216),    # #1D4ED8
            [float]64)
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }

    # 挂锁，按内容框（居中缩放）内的比例坐标绘制
    $cw = $s * $contentScale
    $ox = ($s - $cw) / 2.0
    $oy = ($s - $cw) / 2.0
    $white = [System.Drawing.Brushes]::White
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 29, 78, 216))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float]($cw * 0.085))
    $pen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Center
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    # 锁环：上半圆 + 两条短腿
    $cx = $ox + 0.50 * $cw; $cy = $oy + 0.38 * $cw; $r = 0.15 * $cw
    $g.DrawArc($pen, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r), 180, 180)
    $legBottom = $oy + 0.46 * $cw
    $g.DrawLine($pen, [float]($cx - $r), [float]$cy, [float]($cx - $r), [float]$legBottom)
    $g.DrawLine($pen, [float]($cx + $r), [float]$cy, [float]($cx + $r), [float]$legBottom)

    # 锁体
    $bodyPath = New-RoundedRectPath ([float]($ox + 0.22 * $cw)) ([float]($oy + 0.46 * $cw)) `
        ([float](0.56 * $cw)) ([float](0.40 * $cw)) ([float](0.07 * $cw))
    $g.FillPath($white, $bodyPath)
    $bodyPath.Dispose()

    # 锁孔
    $khR = 0.055 * $cw
    $g.FillEllipse($blue, [float]($ox + 0.50 * $cw - $khR), [float]($oy + 0.575 * $cw - $khR), [float](2 * $khR), [float](2 * $khR))
    $g.FillRectangle($blue, [float]($ox + 0.50 * $cw - $khR * 0.55), [float]($oy + 0.615 * $cw), [float]($khR * 1.1), [float](0.095 * $cw))

    $pen.Dispose(); $blue.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, [string]$dir, [string]$name) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $path = Join-Path $dir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $path ($($bmp.Width)x$($bmp.Height))"
}

# ---- Android 启动图标（legacy：带背景）----
$densities = @{ 'mipmap-mdpi' = 48; 'mipmap-hdpi' = 72; 'mipmap-xhdpi' = 96; 'mipmap-xxhdpi' = 144; 'mipmap-xxxhdpi' = 192 }
Write-Host 'Android launcher:'
foreach ($k in $densities.Keys) {
    $bmp = New-LockBitmap $densities[$k] 0.62 $true
    Save-Png $bmp (Join-Path $androidRes $k) 'ic_launcher.png'
    $bmp.Dispose()
}

# ---- Android 自适应图标前景层（透明底，内容限制在 66% 安全区内）----
$fgSizes = @{ 'mipmap-mdpi' = 108; 'mipmap-hdpi' = 162; 'mipmap-xhdpi' = 216; 'mipmap-xxhdpi' = 324; 'mipmap-xxxhdpi' = 432 }
Write-Host 'Android adaptive foreground:'
foreach ($k in $fgSizes.Keys) {
    $bmp = New-LockBitmap $fgSizes[$k] 0.50 $false
    Save-Png $bmp (Join-Path $androidRes $k) 'ic_launcher_foreground.png'
    $bmp.Dispose()
}

# ---- PC 多尺寸 ico（PNG 条目，Vista+ 均支持）----
Write-Host 'Desktop app.ico:'
$icoSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$pngBytesList = @()
foreach ($sz in $icoSizes) {
    $bmp = New-LockBitmap $sz 0.62 $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngBytesList += , $ms.ToArray()
    $ms.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $sz = $icoSizes[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bytes = $pngBytesList[$i]
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $pngBytesList) { $bw.Write($bytes) }
$bw.Flush()
$icoPath = Join-Path $desktopDir 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
Write-Host "  $icoPath ($($ico.Length) bytes)"
$bw.Dispose(); $ico.Dispose()

Write-Host 'Done.'
