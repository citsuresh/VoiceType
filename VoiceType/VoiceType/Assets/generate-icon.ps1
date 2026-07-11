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

    $capsule = New-RoundedRect 102 30 52 104 26
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
    $darkOuter = & $mkPen $darkColor 22
    $g.DrawArc($darkOuter, 70, 6, 140, 140, -32, 64)    # right outer
    $g.DrawArc($darkOuter, 46, 6, 140, 140, 148, 64)    # left outer
    $darkOuter.Dispose()

    # --- White top layer ---
    foreach ($p in @($capsule, $stem, $base)) { $g.FillPath($whiteBrush, $p) }

    $whiteCradle = & $mkPen ([System.Drawing.Color]::White) 24
    $g.DrawArc($whiteCradle, 84, 78, 88, 88, 0, 180)
    $whiteCradle.Dispose()
    $whiteInner = & $mkPen ([System.Drawing.Color]::White) 15
    $g.DrawArc($whiteInner, 98, 30, 84, 84, -26, 52)
    $g.DrawArc($whiteInner, 74, 30, 84, 84, 154, 52)
    $whiteInner.Dispose()
    $whiteOuter = & $mkPen ([System.Drawing.Color]::White) 14
    $g.DrawArc($whiteOuter, 70, 6, 140, 140, -32, 64)
    $g.DrawArc($whiteOuter, 46, 6, 140, 140, 148, 64)
    $whiteOuter.Dispose()

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
