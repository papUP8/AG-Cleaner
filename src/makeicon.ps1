param([string]$Src = "", [string]$Out = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Src -eq "") { $Src = Join-Path $root "assets\raw.png" }
if ($Out -eq "") { $Out = Join-Path $root "assets\app.ico" }
Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Image]::FromFile($Src)
$iw = [int]$img.Width
$ih = [int]$img.Height
$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]([Math]::Max(3, $s * 0.22))
    $d = 2 * $r
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $edge = $s - 1
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($edge - $d, 0, $d, $d, 270, 90)
    $path.AddArc($edge - $d, $edge - $d, $d, $d, 0, 90)
    $path.AddArc(0, $edge - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path)
    $inset = [int]($iw * 0.02)
    $sw = $iw - (2 * $inset)
    $sh = $ih - (2 * $inset)
    $dest = [System.Drawing.Rectangle]::new(0, 0, $s, $s)
    $srcRect = [System.Drawing.Rectangle]::new($inset, $inset, $sw, $sh)
    $g.DrawImage($img, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $tmp = Join-Path $env:TEMP ("ico_" + $s + ".png")
    $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += $tmp
}
$img.Dispose()

$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
$dataList = @()
foreach ($i in 0..($sizes.Count - 1)) {
    $bytes = [System.IO.File]::ReadAllBytes($pngs[$i])
    $s = $sizes[$i]
    $w = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$w); $bw.Write([Byte]$w); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
    $dataList += , $bytes
}
foreach ($b in $dataList) { $bw.Write($b) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
foreach ($p in $pngs) { Remove-Item $p -Force }
Write-Output ("ICO OK: " + $Out + " " + (Get-Item $Out).Length + " bytes")
