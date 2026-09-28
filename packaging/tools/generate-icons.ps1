<#
.SYNOPSIS
    Regenerates every committed icon asset from the single source icon, <repo>/icon.png.
.DESCRIPTION
    One PNG at the repository root is the only icon source. Every other icon file the
    packages need is derived from it and checked in, so CI runners - whose PowerShell has
    no System.Drawing.Common - never resize anything:

      packaging/assets/icons/hicolor/<size>x<size>/apps/org.mutantcat.electronicpointer.png
                                      the seven sizes the deb and the AppImage install
      packaging/windows/assets/*.png    the MSIX tiles (44, 50, 150, 310, wide 310x150)
      packaging/windows/assets/electronicpointer.ico
                                      multi resolution icon used by the NSIS installer,
                                      the shortcuts it creates and the uninstall entry

    Run it after replacing icon.png. Needs Windows PowerShell with the .NET System.Drawing
    assembly, which every desktop machine already has and hosted runners do not.
.EXAMPLE
    pwsh packaging/tools/generate-icons.ps1
#>
[CmdletBinding()]
param(
    # Defaults to <repo>/icon.png.
    [string]$Source,

    # Defaults to the repository root, two levels above this script.
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $RepoRoot 'icon.png'
}
if (-not (Test-Path -LiteralPath $Source)) {
    throw "icon source not found: $Source"
}

Add-Type -AssemblyName System.Drawing

$AppId = 'org.mutantcat.electronicpointer'
$HicolorRoot = Join-Path $RepoRoot "packaging\assets\icons\hicolor"
$WindowsAssets = Join-Path $RepoRoot 'packaging\windows\assets'

function New-Resampled {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height
    )
    # The source is fully opaque paper white, so the canvas is filled before the resample
    # and bicubic keeps the pencil outline readable when it is shrunk to 16 pixels.
    $result = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($result)
        try {
            $graphics.Clear([System.Drawing.Color]::White)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.DrawImage($Bitmap, (New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)))
        }
        finally {
            $graphics.Dispose()
        }
    }
    catch {
        $result.Dispose()
        throw
    }
    return $result
}

function Save-BitmapAsPng {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][string]$Path
    )
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("  {0}  ({1} bytes)" -f $Path.Replace("$RepoRoot\", ''), (Get-Item -LiteralPath $Path).Length)
}

function New-PaddedBitmap {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height
    )
    # A white plate with the square icon centred, which is what the wide MSIX tile wants:
    # a 310x150 canvas holding a 150x150 icon in the middle.
    $plate = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($plate)
        try {
            $graphics.Clear([System.Drawing.Color]::White)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $side = [Math]::Min($Width, $Height)
            $scaled = New-Resampled -Bitmap $Bitmap -Width $side -Height $side
            try {
                $graphics.DrawImage($scaled, (New-Object System.Drawing.Rectangle(
                    [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)))
            }
            finally {
                $scaled.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
    }
    catch {
        $plate.Dispose()
        throw
    }
    return $plate
}

function Save-IconFile {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][int[]]$Sizes,
        [Parameter(Mandatory = $true)][string]$Path
    )
    # Each entry is a complete PNG, which Vista and later read straight out of the .ico.
    # Small sizes go through a 4x intermediate first: one bicubic pass from 1024 down to
    # 16 turns thin outlines into mush.
    $entries = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $Sizes) {
        $intermediate = New-Resampled -Bitmap $Bitmap -Width ($size * 4) -Height ($size * 4)
        try {
            $stepped = New-Resampled -Bitmap $intermediate -Width $size -Height $size
            try {
                $stream = New-Object System.IO.MemoryStream
                try {
                    $stepped.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                    $entries.Add($stream.ToArray())
                }
                finally {
                    $stream.Dispose()
                }
            }
            finally {
                $stepped.Dispose()
            }
        }
        finally {
            $intermediate.Dispose()
        }
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        # ICONDIR: reserved, type 1 (icon), count.
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)
        $offset = 6 + (16 * $Sizes.Count)
        for ($index = 0; $index -lt $Sizes.Count; $index++) {
            $size = $Sizes[$index]
            $data = $entries[$index]
            # Width and height are single bytes and 256 is stored as 0.
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)   # palette size, unused for true colour
            $writer.Write([byte]0)   # reserved
            $writer.Write([uint16]1) # colour planes
            $writer.Write([uint16]32)
            $writer.Write([uint32]$data.Length)
            $writer.Write([uint32]$offset)
            $offset += $data.Length
        }
        foreach ($data in $entries) {
            $writer.Write($data)
        }
        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
    }
    Write-Host ("  {0}  ({1} bytes)" -f $Path.Replace("$RepoRoot\", ''), (Get-Item -LiteralPath $Path).Length)
}

$SourceBitmap = New-Object System.Drawing.Bitmap($Source)
try {
    Write-Host "source: $Source ($($SourceBitmap.Width)x$($SourceBitmap.Height), $($SourceBitmap.PixelFormat))"

    Write-Host "hicolor icons:"
    foreach ($size in 16, 24, 32, 48, 64, 128, 256) {
        $bitmap = New-Resampled -Bitmap $SourceBitmap -Width $size -Height $size
        try {
            Save-BitmapAsPng -Bitmap $bitmap -Path (Join-Path $HicolorRoot "${size}x${size}\apps\$AppId.png")
        }
        finally {
            $bitmap.Dispose()
        }
    }

    Write-Host "MSIX tiles:"
    foreach ($size in 44, 50, 150, 310) {
        $bitmap = New-Resampled -Bitmap $SourceBitmap -Width $size -Height $size
        try {
            $name = switch ($size) {
                44 { 'Square44x44Logo' }
                50 { 'StoreLogo' }
                150 { 'Square150x150Logo' }
                default { 'Square310x310Logo' }
            }
            Save-BitmapAsPng -Bitmap $bitmap -Path (Join-Path $WindowsAssets "$name.png")
        }
        finally {
            $bitmap.Dispose()
        }
    }
    $wide = New-PaddedBitmap -Bitmap $SourceBitmap -Width 310 -Height 150
    try {
        Save-BitmapAsPng -Bitmap $wide -Path (Join-Path $WindowsAssets 'Wide310x150Logo.png')
    }
    finally {
        $wide.Dispose()
    }

    Write-Host "installer icon:"
    Save-IconFile -Bitmap $SourceBitmap -Sizes 16, 24, 32, 48, 64, 128, 256 -Path (Join-Path $WindowsAssets 'electronicpointer.ico')
}
finally {
    $SourceBitmap.Dispose()
}

Write-Host ""
Write-Host "every icon asset now derives from $Source"
