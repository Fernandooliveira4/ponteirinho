# Prepara as mãos do TinyHand (licença MIT) para o Ponteirinho:
# recorta as bordas transparentes e reduz para 60%, gravando em Assets\tinyhand\.
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\..\arte\tinyhand-originais"),
    [string]$Dest = (Join-Path $PSScriptRoot "..\Assets\tinyhand"),
    [double]$Scale = 0.6
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing, System.Drawing.Primitives -TypeDefinition @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class AlphaBox {
    // Retângulo dos pixels com alfa > 8.
    public static Rectangle Find(Bitmap bmp) {
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var d = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[d.Stride * bmp.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (px[y * d.Stride + x * 4 + 3] > 8) {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        return maxX < 0 ? r : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}
"@

New-Item -ItemType Directory -Force $Dest | Out-Null
foreach ($f in Get-ChildItem $Source -Filter "hand_*.png") {
    $src = New-Object System.Drawing.Bitmap $f.FullName
    $box = [AlphaBox]::Find($src)
    $box.Inflate(4, 4)
    $box.Intersect((New-Object System.Drawing.Rectangle 0, 0, $src.Width, $src.Height))
    $w = [int][Math]::Round($box.Width * $Scale); $h = [int][Math]::Round($box.Height * $Scale)
    $out = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $box, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $src.Dispose()
    $out.Save((Join-Path $Dest $f.Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}
Copy-Item (Join-Path $Source "LICENSE-TinyHand.txt") $Dest -Force
$files = Get-ChildItem $Dest -Filter *.png
"{0} maos preparadas, total {1:N1} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB)
