# Generates Resources\app.ico (multi-resolution, PNG-compressed entries).
# Run once; the produced .ico is committed with the source so no build-time
# dependency on this script exists.
Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot "..\src\CloudflareR2Uploader\Resources\app.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square with a violet gradient.
    $pad = [double]$s * 0.045
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad))
    $radius = [double]$s * 0.235
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($radius * 2)
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 167, 139, 250),
        [System.Drawing.Color]::FromArgb(255, 91, 33, 182),
        [float]62)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # Soft inner highlight along the top edge.
    if ($s -ge 32) {
        $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.RectangleF($rect.X, $rect.Y, $rect.Width, $rect.Height * 0.55)),
            [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
            [float]90)
        $old = $g.Clip
        $g.SetClip($path)
        $g.FillRectangle($hl, $rect.X, $rect.Y, $rect.Width, $rect.Height * 0.55)
        $g.Clip = $old
        $hl.Dispose()
    }

    # Upward arrow (the "upload" mark).
    $cx = [double]$s / 2.0
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    $headHalf = [double]$s * 0.215
    $headTop = [double]$s * 0.235
    $headBottom = [double]$s * 0.50
    $arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Drawing.PointF[]' 3
    $pts[0] = New-Object System.Drawing.PointF([float]$cx, [float]$headTop)
    $pts[1] = New-Object System.Drawing.PointF([float]($cx - $headHalf), [float]$headBottom)
    $pts[2] = New-Object System.Drawing.PointF([float]($cx + $headHalf), [float]$headBottom)
    $arrow.AddPolygon($pts)
    $g.FillPath($white, $arrow)
    $arrow.Dispose()

    $stemHalf = [double]$s * 0.082
    $g.FillRectangle($white, [float]($cx - $stemHalf), [float]($headBottom - $s * 0.02), [float]($stemHalf * 2), [float]($s * 0.18))

    # Base line, echoing a "drop target".
    $baseW = [double]$s * 0.46
    $baseH = [math]::Max(2.0, [double]$s * 0.075)
    $baseY = [double]$s * 0.715
    $baseRect = New-Object System.Drawing.RectangleF([float]($cx - $baseW / 2), [float]$baseY, [float]$baseW, [float]$baseH)
    $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $br = [float]($baseH)
    $bp.AddArc($baseRect.X, $baseRect.Y, $br, $br, 90, 180)
    $bp.AddArc($baseRect.Right - $br, $baseRect.Y, $br, $br, 270, 180)
    $bp.CloseFigure()
    $g.FillPath($white, $bp)
    $bp.Dispose()

    $white.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

# Sizes up to 128 are stored as classic 32bpp BGRA DIB entries because
# System.Drawing.Icon cannot decode PNG-compressed entries. 256x256 is stored as
# PNG, which is the required format at that size and is only consumed by the shell.
function Get-DibEntry([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle(0, 0, $w, $h)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object 'Byte[]' ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    $maskStride = [int]([math]::Floor((($w + 31) / 32))) * 4
    $xorSize = $w * $h * 4
    $andSize = $maskStride * $h

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)            # biSize
    $bw.Write([Int32]$w)             # biWidth
    $bw.Write([Int32]($h * 2))       # biHeight (XOR + AND)
    $bw.Write([UInt16]1)             # biPlanes
    $bw.Write([UInt16]32)            # biBitCount
    $bw.Write([UInt32]0)             # biCompression = BI_RGB
    $bw.Write([UInt32]($xorSize + $andSize))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    # XOR bitmap, bottom-up.
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($raw, $y * $stride, $w * 4) }
    # AND mask: all zero; the 32bpp alpha channel carries transparency.
    $bw.Write((New-Object 'Byte[]' $andSize))

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Close(); $ms.Dispose()
    return , $bytes   # leading comma stops PowerShell unrolling the byte array
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose()
    }
    else {
        $bytes = Get-DibEntry $bmp
    }
    $pngs += , @{ Size = $s; Bytes = $bytes }
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$pngs.Count)

$offset = 6 + (16 * $pngs.Count)
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([Byte]$dim)            # width
    $bw.Write([Byte]$dim)            # height
    $bw.Write([Byte]0)               # palette colours
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # colour planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$p.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Flush(); $bw.Close(); $fs.Dispose()

Write-Output ("Wrote {0} ({1} bytes, {2} images)" -f (Resolve-Path $outPath), (Get-Item $outPath).Length, $pngs.Count)
