# Generates src\asusmon.ico: a flat monitor glyph, drawn from scratch so the
# project ships its own artwork rather than a Windows system icon resource.
# Run with Windows PowerShell (powershell.exe) for GDI+.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$output = Join-Path $PSScriptRoot '..\src\asusmon.ico'

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h))
        return $path
    }
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $path.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $silver = [System.Drawing.Color]::FromArgb(255, 209, 214, 222)
    $screenTop = [System.Drawing.Color]::FromArgb(255, 56, 152, 255)
    $screenBottom = [System.Drawing.Color]::FromArgb(255, 0, 82, 184)

    # Panel body. Below 20 px the stand is dropped, so the panel is enlarged and
    # recentred to fill the tile instead of leaving dead space beneath it.
    if ($s -ge 20) {
        $px = $s * 0.045
        $py = $s * 0.12
        $pw = $s * 0.91
        $ph = $s * 0.60
    }
    else {
        $px = $s * 0.02
        $py = $s * 0.16
        $pw = $s * 0.96
        $ph = $s * 0.68
    }

    $panel = New-RoundedPath $px $py $pw $ph ($s * 0.09)
    $brush = New-Object System.Drawing.SolidBrush $silver
    $g.FillPath($brush, $panel)
    $brush.Dispose()

    # Screen. The inset scales with size so it stays visible at 16 px.
    $inset = [Math]::Max(1.0, $s * 0.075)
    $sx = $px + $inset
    $sy = $py + $inset
    $sw = $pw - ($inset * 2)
    $sh = $ph - ($inset * 2)
    $screen = New-RoundedPath $sx $sy $sw $sh ($s * 0.035)
    $rect = New-Object System.Drawing.RectangleF $sx, $sy, $sw, $sh
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $screenTop, $screenBottom, 90.0
    $g.FillPath($grad, $screen)
    $grad.Dispose()
    $screen.Dispose()
    $panel.Dispose()

    # Stand: neck then base. Dropped below 20 px, where they turn to mush.
    if ($s -ge 20) {
        $standBrush = New-Object System.Drawing.SolidBrush $silver
        $nw = $s * 0.17
        $neck = New-Object System.Drawing.RectangleF (($s - $nw) / 2), ($py + $ph - 1), $nw, ($s * 0.12)
        $g.FillRectangle($standBrush, $neck)

        $bw = $s * 0.46
        $base = New-RoundedPath (($s - $bw) / 2) ($s * 0.85) $bw ($s * 0.075) ($s * 0.03)
        $g.FillPath($standBrush, $base)
        $base.Dispose()
        $standBrush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# ICO container: 6-byte header, one 16-byte directory entry per image, then the
# payloads.
#
# Sizes up to 64 px are written as DIB (BITMAPINFOHEADER + bottom-up BGRA + AND
# mask), which every Windows icon consumer understands. PNG frames are legal
# since Vista but are rejected by some consumers, notably System.Drawing.Icon,
# so they are used only for 128 and 256 px where the size saving matters.

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $locked = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $pixels = New-Object byte[] ($locked.Stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($locked)

    $maskStride = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out

    $w.Write([uint32]40)              # biSize
    $w.Write([int32]$s)               # biWidth
    $w.Write([int32]($s * 2))         # biHeight: XOR and AND stacked
    $w.Write([uint16]1)               # biPlanes
    $w.Write([uint16]32)              # biBitCount
    $w.Write([uint32]0)               # biCompression: BI_RGB
    $w.Write([uint32](($s * $s * 4) + ($maskStride * $s)))
    $w.Write([int32]0); $w.Write([int32]0)
    $w.Write([uint32]0); $w.Write([uint32]0)

    # Colour data, bottom-up.
    for ($y = $s - 1; $y -ge 0; $y--) {
        $w.Write($pixels, ($y * $locked.Stride), ($s * 4))
    }

    # AND mask, bottom-up: 1 = transparent. Redundant with the alpha channel but
    # required to be present, and some consumers still honour it.
    for ($y = $s - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride

        for ($x = 0; $x -lt $s; $x++) {
            if ($pixels[($y * $locked.Stride) + ($x * 4) + 3] -lt 128) {
                $byte = [int][Math]::Floor($x / 8)
                $row[$byte] = $row[$byte] -bor (0x80 -shr ($x % 8))
            }
        }

        $w.Write($row)
    }

    $w.Flush()
    $bytes = $out.ToArray()
    $w.Dispose()
    $out.Dispose()
    return , $bytes
}

$payloads = @()

foreach ($size in $sizes) {
    $bmp = New-Frame $size

    if ($size -ge 128) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $payloads += , $ms.ToArray()
        $ms.Dispose()
    }
    else {
        $payloads += , (Get-DibBytes $bmp)
    }

    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out

$w.Write([uint16]0)               # reserved
$w.Write([uint16]1)               # type: icon
$w.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $payloads[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }   # 0 means 256

    $w.Write([byte]$dim)          # width
    $w.Write([byte]$dim)          # height
    $w.Write([byte]0)             # palette entries
    $w.Write([byte]0)             # reserved
    $w.Write([uint16]1)           # colour planes
    $w.Write([uint16]32)          # bits per pixel
    $w.Write([uint32]$data.Length)
    $w.Write([uint32]$offset)

    $offset += $data.Length
}

foreach ($data in $payloads) {
    $w.Write($data)
}

$w.Flush()
$bytes = $out.ToArray()
$w.Dispose()
$out.Dispose()

$full = [System.IO.Path]::GetFullPath($output)
[System.IO.File]::WriteAllBytes($full, $bytes)

Write-Host "wrote $full ($($sizes.Count) sizes, $($bytes.Length) bytes)"
