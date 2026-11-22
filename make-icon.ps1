# Genera corbolprompt.ico (multi-risoluzione, PNG interni) e un'anteprima icon-preview.png
# Disegno: quadrato arrotondato con gradiente verde -> blu notte come le altre app Corbol (riflesso chiaro in alto),
# il prompt ">" bianco e una lambda verde chiaro (il Lisp), con il cursore "_" sotto.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function P([double]$x, [double]$y) { return New-Object System.Drawing.PointF ([float]$x), ([float]$y) }

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(0.5, $s * 0.03)
    $w = $s - 2 * $m
    $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
    $bg = New-RoundRect $m $m $w $w ($s * 0.22)

    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 22, 163, 74)),
        ([System.Drawing.Color]::FromArgb(255, 15, 23, 42)), 45.0
    $g.FillPath($grad, $bg)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)),
        ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
    $g.FillPath($hl, $bg)

    # ">" del prompt: due tratti spessi con estremità arrotondate
    $pw = [Math]::Max(1.6, $s * 0.105)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float]$pw)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $chev = [System.Drawing.PointF[]]@((P ($s * 0.20) ($s * 0.27)), (P ($s * 0.42) ($s * 0.47)), (P ($s * 0.20) ($s * 0.67)))
    $g.DrawLines($pen, $chev)

    # lambda: gamba lunga da in alto a sinistra a in basso a destra, gamba corta verso sinistra
    $lc = [System.Drawing.Color]::FromArgb(255, 134, 239, 172)
    $lp = New-Object System.Drawing.Pen $lc, ([float]([Math]::Max(1.4, $s * 0.085)))
    $lp.StartCap = 'Round'; $lp.EndCap = 'Round'
    $g.DrawLine($lp, (P ($s * 0.55) ($s * 0.22)), (P ($s * 0.82) ($s * 0.70)))
    $g.DrawLine($lp, (P ($s * 0.685) ($s * 0.46)), (P ($s * 0.53) ($s * 0.70)))

    # cursore "_"
    if ($s -ge 20) {
        $cp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([float]([Math]::Max(1.2, $s * 0.06)))
        $cp.StartCap = 'Round'; $cp.EndCap = 'Round'
        $g.DrawLine($cp, (P ($s * 0.22) ($s * 0.80)), (P ($s * 0.46) ($s * 0.80)))
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $b.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $dir 'corbolprompt.ico'), $out.ToArray())

$pw = 20; foreach ($s in $sizes) { $pw += $s + 20 }
$prev = New-Object System.Drawing.Bitmap $pw, 620
$pg = [System.Drawing.Graphics]::FromImage($prev)
$pg.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
$pg.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 310, $pw, 310)
$x = 20
foreach ($s in $sizes) {
    $b = New-IconBitmap $s
    $pg.DrawImage($b, $x, 290 - $s); $pg.DrawImage($b, $x, 600 - $s)
    $x += $s + 20; $b.Dispose()
}
$prev.Save((Join-Path $dir 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Creati corbolprompt.ico e icon-preview.png"
