# Scales the Who Haunts icon in assets\logo-source.png (a square PNG, already cut out on transparency) and writes
#   assets\logo.png  - 256px transparent logo shown in the launcher window
#   assets\icon.ico  - multi-size exe/installer icon
# Rerun after replacing logo-source.png.
param([string] $Source = (Join-Path $PSScriptRoot '..\assets\logo-source.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\assets'
$src = [Drawing.Bitmap]::FromFile((Resolve-Path $Source))

function New-Logo([int] $size) {
    $pad = [math]::Max(0.0, [math]::Floor($size / 64.0))
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($src, [float]$pad, [float]$pad, [float]($size - 2 * $pad), [float]($size - 2 * $pad))
    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()   # leading comma stops PowerShell unrolling the byte array
}

# Classic 32-bit DIB icon entry: BITMAPINFOHEADER, bottom-up BGRA pixels, then a 1-bit AND mask.
# WinForms' Icon class can't read PNG entries, so only the 256px size uses PNG.
function Get-DibBytes($bmp) {
    $n = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $maskStride = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([int]0); $w.Write([int]($n * $n * 4 + $maskStride * $n)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskStride * $n))) # alpha channel does the masking
    $w.Flush()
    return , $ms.ToArray()
}

$logo = New-Logo 256
$logo.Save((Join-Path $assets 'logo.png'), [Drawing.Imaging.ImageFormat]::Png)

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bmp = New-Logo $s
    $images.Add($(if ($s -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }))
}
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'icon.ico'), $ico.ToArray())
$src.Dispose()
Write-Host "Wrote assets\logo.png and assets\icon.ico"
