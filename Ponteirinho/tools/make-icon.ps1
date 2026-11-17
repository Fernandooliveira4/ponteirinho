# Gera Assets\app.ico a partir de uma das artes cinza, pintada com uma cor
# (o mesmo "mapa de gradiente" que o app usa). Rode de novo se trocar a arte.
param(
    [string]$Png = (Join-Path $PSScriptRoot "..\Assets\hand-fofinha.png"),
    [string]$Color = "#B7A4F2",
    [string]$Ico = (Join-Path $PSScriptRoot "..\Assets\app.ico")
)

Add-Type -AssemblyName System.Drawing

# 1) Carrega a arte e pinta os pixels cinza.
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))
$art = New-Object System.Drawing.Bitmap $src
$src.Dispose()
$c = [System.Drawing.ColorTranslator]::FromHtml($Color)
$rect = New-Object System.Drawing.Rectangle 0, 0, $art.Width, $art.Height
$data = $art.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $art.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$dr = $c.R * 0.12; $dg = $c.G * 0.12; $db = $c.B * 0.12
for ($i = 0; $i -lt $bytes.Length; $i += 4) {
    if ($bytes[$i + 3] -eq 0) { continue }
    $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
    $max = [Math]::Max($r, [Math]::Max($g, $b)); $min = [Math]::Min($r, [Math]::Min($g, $b))
    if ($max - $min -gt 14) { continue }
    $l = ($r + $g + $b) / 765.0
    if ($l -le 0.5) { $t = $l / 0.5; $nr = $dr + ($c.R - $dr) * $t; $ng = $dg + ($c.G - $dg) * $t; $nb = $db + ($c.B - $db) * $t }
    else { $t = ($l - 0.5) / 0.5; $nr = $c.R + (255 - $c.R) * $t; $ng = $c.G + (255 - $c.G) * $t; $nb = $c.B + (255 - $c.B) * $t }
    $bytes[$i] = [byte][Math]::Round($nb); $bytes[$i + 1] = [byte][Math]::Round($ng); $bytes[$i + 2] = [byte][Math]::Round($nr)
}
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
$art.UnlockBits($data)

# 2) Gera os tamanhos do ícone.
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $gr = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gr.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gr.Clear([System.Drawing.Color]::Transparent)
    $scale = [Math]::Min($size / $art.Width, $size / $art.Height)
    $w = [int]($art.Width * $scale); $h = [int]($art.Height * $scale)
    $gr.DrawImage($art, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
    $gr.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
}
$art.Dispose()

# 3) Formato ICO: cabeçalho + diretório + imagens PNG.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f.Bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Ico, $out.ToArray())
Write-Host "Icone gerado: $Ico"
