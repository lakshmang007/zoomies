# Draws the FolderPilot icon (blue tile, white folder, navigation arrow) at every size Windows asks for,
# then packs the PNGs into a multi-resolution .ico. Run with Windows PowerShell:
#   powershell -ExecutionPolicy Bypass -File assets\make-icon.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\FolderPilot\Assets"))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [float]$size

    # Tile
    $inset = [Math]::Max(0.5, $s * 0.03)
    $tile = New-RoundedRect $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 86, 140, 255)), ([System.Drawing.Color]::FromArgb(255, 37, 76, 214))
    $g.FillPath($grad, $tile)

    # Folder: back with tab, then the front panel
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 227, 255))
    $fx = $s * 0.20; $fy = $s * 0.27; $fw = $s * 0.60; $fh = $s * 0.48
    $back = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tabW = $fw * 0.40; $tabH = $fh * 0.16
    $back.AddPolygon(@(
        (New-Object System.Drawing.PointF $fx, ($fy + $tabH)),
        (New-Object System.Drawing.PointF $fx, $fy),
        (New-Object System.Drawing.PointF ($fx + $tabW), $fy),
        (New-Object System.Drawing.PointF ($fx + $tabW + $tabH), ($fy + $tabH)),
        (New-Object System.Drawing.PointF ($fx + $fw), ($fy + $tabH)),
        (New-Object System.Drawing.PointF ($fx + $fw), ($fy + $fh)),
        (New-Object System.Drawing.PointF $fx, ($fy + $fh))
    ))
    $g.FillPath($soft, $back)
    $front = New-RoundedRect $fx ($fy + $fh * 0.28) $fw ($fh * 0.72) ([Math]::Max(1, $s * 0.04))
    $g.FillPath($white, $front)

    # Navigation arrow on the folder front (dropped at tiny sizes where it would be a smudge)
    if ($size -ge 24) {
        $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 47, 98, 235))
        $cx = $fx + $fw * 0.5; $cy = $fy + $fh * 0.64; $r = $fh * 0.25
        $arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
        $arrow.AddPolygon(@(
            (New-Object System.Drawing.PointF ($cx + $r), ($cy - $r * 0.95)),
            (New-Object System.Drawing.PointF ($cx - $r * 0.15), ($cy + $r * 0.95)),
            (New-Object System.Drawing.PointF ($cx - $r * 0.05), ($cy + $r * 0.1)),
            (New-Object System.Drawing.PointF ($cx - $r * 0.95), ($cy - $r * 0.05))
        ))
        $g.FillPath($blue, $arrow)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = Render $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($size -eq 256) { $bmp.Save((Join-Path $OutDir "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO container: header, one directory entry per image, then the PNG payloads.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "FolderPilot.ico"), $out.ToArray())
Write-Output "Wrote $(Join-Path $OutDir 'FolderPilot.ico') and logo.png"
