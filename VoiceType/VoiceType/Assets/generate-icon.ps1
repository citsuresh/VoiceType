Add-Type -AssemblyName System.Drawing

function New-RoundedRect {
    param([double]$x, [double]$y, [double]$w, [double]$h, [double]$r)
    $d = $r * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Glyph {
    param([System.Drawing.Graphics]$g)

    $capsule = New-RoundedRect 102 30 52 116 26
    $stem = New-RoundedRect 122 164 12 26 6
    $base = New-RoundedRect 98 188 60 14 7

    $darkBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 31, 31, 31))
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    $mkPen = {
        param($color, $width)
        $pen = New-Object System.Drawing.Pen $color, $width
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        return $pen
    }
    $darkColor = [System.Drawing.Color]::FromArgb(255, 31, 31, 31)

    # --- Dark base layer ---
    $darkOutline = & $mkPen $darkColor 8
    foreach ($p in @($capsule, $stem, $base)) {
        $g.FillPath($darkBrush, $p)
        $g.DrawPath($darkOutline, $p)
    }
    $darkOutline.Dispose()

    $darkCradle = & $mkPen $darkColor 32
    $g.DrawArc($darkCradle, 84, 78, 88, 88, 0, 180)
    $darkCradle.Dispose()
    $darkInner = & $mkPen $darkColor 23
    $g.DrawArc($darkInner, 98, 30, 84, 84, -26, 52)     # right inner
    $g.DrawArc($darkInner, 74, 30, 84, 84, 154, 52)     # left inner
    $darkInner.Dispose()

    # --- White top layer ---
    foreach ($p in @($capsule, $stem, $base)) { $g.FillPath($whiteBrush, $p) }

    $whiteCradle = & $mkPen ([System.Drawing.Color]::White) 24
    $g.DrawArc($whiteCradle, 84, 78, 88, 88, 0, 180)
    $whiteCradle.Dispose()
    $whiteInner = & $mkPen ([System.Drawing.Color]::White) 15
    $g.DrawArc($whiteInner, 98, 30, 84, 84, -26, 52)
    $g.DrawArc($whiteInner, 74, 30, 84, 84, 154, 52)
    $whiteInner.Dispose()

    $darkBrush.Dispose(); $whiteBrush.Dispose()
    $capsule.Dispose(); $stem.Dispose(); $base.Dispose()
}

function Render-Png {
    param([int]$size)
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)
    # Enlarge the glyph so it reads at a similar size to native Windows tray
    # glyphs. Measured raw bounds: x 63..193 (w 131), y 26..206 (h 181), center
    # (128, 116). Height is the limiting dimension; zoom about the glyph center
    # to nearly fill the 256 canvas (small anti-alias margin), then shift the
    # glyph center to the canvas center (128, 128).
    $zoom = 1.38
    $g.TranslateTransform(0.0, 12.0)
    $g.TranslateTransform(128.0, 116.0)
    $g.ScaleTransform($zoom, $zoom)
    $g.TranslateTransform(-128.0, -116.0)
    Draw-Glyph $g
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($sz in $sizes) { $frames += , (Render-Png $sz) }

$outPath = Join-Path $PSScriptRoot 'voicetype.ico'
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter $fs

# ICONDIR
$bw.Write([UInt16]0)             # reserved
$bw.Write([UInt16]1)             # type = icon
$bw.Write([UInt16]$sizes.Count)  # count

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $frames[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([Byte]$dim)        # width
    $bw.Write([Byte]$dim)        # height
    $bw.Write([Byte]0)           # colors
    $bw.Write([Byte]0)           # reserved
    $bw.Write([UInt16]1)         # planes
    $bw.Write([UInt16]32)        # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "Created $outPath"
