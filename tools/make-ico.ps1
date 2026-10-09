# Генерирует Copeer.ico (16..256 px, PNG-компрессия) тем же дизайном, что и иконка в трее:
# скруглённый квадрат с сине-фиолетовым градиентом и белой «C».
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $r = [Math]::Max(3, [int]($s * 0.30))
    $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 180)
    $path.AddArc($s - $d, 0, $d, $d, 270, 180)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 180)
    $path.AddArc(0, $s - $d, $d, $d, 90, 180)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, `
        [System.Drawing.Color]::FromArgb(0x4A, 0x9E, 0xFF), `
        [System.Drawing.Color]::FromArgb(0x7B, 0x4A, 0xE2), 45.0)
    $g.FillPath($brush, $path)

    $font = New-Object System.Drawing.Font('Segoe UI', [Math]::Max(8, [int]($s * 0.58)), `
        [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString('C', $font, [System.Drawing.Brushes]::White, `
        (New-Object System.Drawing.RectangleF(0, (-$s * 0.04), $s, $s)), $sf)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
}

$fs = [System.IO.File]::Create((Join-Path $root 'Copeer.ico'))
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $sz = if ($p.Size -ge 256) { 0 } else { [byte]$p.Size }
    $bw.Write([byte]$sz); $bw.Write([byte]$sz); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Dispose()
Write-Host 'Copeer.ico created'
