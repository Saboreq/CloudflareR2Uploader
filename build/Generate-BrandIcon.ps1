[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..'))
$outputPath = Join-Path -Path $repositoryRoot -ChildPath 'src\CloudflareR2Uploader\Resources\app.ico'
$wpfOutputPath = Join-Path -Path $repositoryRoot -ChildPath 'src\CloudflareR2Uploader.Wpf\Resources\app.ico'

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-SPath {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.StartFigure()
    $path.AddBezier(12, 10.5, 12, 9.3, 13, 8.5, 14.6, 8.5)
    $path.AddBezier(14.6, 8.5, 16.3, 8.5, 17.3, 9.4, 17.5, 10.7)
    $path.AddLine(17.5, 10.7, 19.8, 10.7)
    $path.AddBezier(19.8, 10.7, 19.6, 8.3, 17.8, 6.7, 14.6, 6.7)
    $path.AddBezier(14.6, 6.7, 11.4, 6.7, 9.6, 8.4, 9.6, 10.8)
    $path.AddBezier(9.6, 10.8, 9.6, 15.4, 17.2, 13.8, 17.2, 16.8)
    $path.AddBezier(17.2, 16.8, 17.2, 18.1, 16.1, 18.9, 14.3, 18.9)
    $path.AddBezier(14.3, 18.9, 12.4, 18.9, 11.3, 17.9, 11.2, 16.5)
    $path.AddLine(11.2, 16.5, 8.8, 16.5)
    $path.AddBezier(8.8, 16.5, 8.9, 19.1, 11, 20.7, 14.3, 20.7)
    $path.AddBezier(14.3, 20.7, 17.7, 20.7, 19.7, 19, 19.7, 16.5)
    $path.AddBezier(19.7, 16.5, 19.7, 11.8, 12, 13.4, 12, 10.5)
    $path.CloseFigure()
    return $path
}

function New-BrandPngBytes {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.ScaleTransform($Size / 32.0, $Size / 32.0)

        $tile = New-RoundedRectanglePath -Bounds ([System.Drawing.RectangleF]::new(0, 0, 32, 32)) -Radius 7
        $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0b0b10'))
        $sPath = New-SPath
        $sBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#f7f7f8'))
        $slash = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#8b5cf6'), 1.6)
        $slash.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $slash.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        try {
            $graphics.FillPath($tileBrush, $tile)
            $graphics.FillPath($sBrush, $sPath)
            $graphics.DrawLine($slash, 21, 8, 17, 24)
        }
        finally {
            $slash.Dispose()
            $sBrush.Dispose()
            $sPath.Dispose()
            $tileBrush.Dispose()
            $tile.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            # Prevent PowerShell from unrolling the byte array into individual pipeline items.
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Data = New-BrandPngBytes -Size $size
    }
}

$outputDirectory = Split-Path -Path $outputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$file = [System.IO.File]::Open(
    $outputPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = [System.IO.BinaryWriter]::new($file)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -ge 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame.Data)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$wpfOutputDirectory = Split-Path -Path $wpfOutputPath -Parent
if (-not (Test-Path -LiteralPath $wpfOutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $wpfOutputDirectory | Out-Null
}
Copy-Item -LiteralPath $outputPath -Destination $wpfOutputPath -Force

Write-Host "Generated Saboreq brand icons: $outputPath and $wpfOutputPath"
