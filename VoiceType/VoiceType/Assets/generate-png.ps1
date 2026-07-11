Add-Type -AssemblyName System.Drawing

# Reuse the exact glyph rendering used by generate-icon.ps1

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
    $g.DrawArc($darkInner, 98, 30, 84, 84, -26, 52)
    $g.DrawArc($darkInner, 74, 30, 84, 84, 154, 52)
    $darkInner.Dispose()
    $darkOuter = & $mkPen $darkColor 22
    $g.DrawArc($darkOuter, 70, 6, 140, 140, -32, 64)
    $g.DrawArc($darkOuter, 46, 6, 140, 140, 148, 64)
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

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$s = $size / 256.0
$g.ScaleTransform($s, $s)
Draw-Glyph $g
$g.Dispose()

$outPath = Join-Path $PSScriptRoot 'voicetype.png'
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Created $outPath"
