param(
    [Parameter(Mandatory = $true)][string]$InputPng,
    [Parameter(Mandatory = $true)][string]$OutputIco
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeIconMethods {
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
}
'@

$source = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $InputPng).Path)
$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$handle = [IntPtr]::Zero
$icon = $null
$stream = $null

try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

    $ratio = [Math]::Min(232.0 / $source.Width, 232.0 / $source.Height)
    $width = [int]($source.Width * $ratio)
    $height = [int]($source.Height * $ratio)
    $left = [int]((256 - $width) / 2)
    $top = [int]((256 - $height) / 2)
    $graphics.DrawImage($source, $left, $top, $width, $height)

    $outputDirectory = Split-Path -Parent $OutputIco
    if ($outputDirectory) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    $handle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($OutputIco)
    $icon.Save($stream)
}
finally {
    if ($stream) { $stream.Dispose() }
    if ($icon) { $icon.Dispose() }
    if ($handle -ne [IntPtr]::Zero) { [NativeIconMethods]::DestroyIcon($handle) | Out-Null }
    $graphics.Dispose()
    $bitmap.Dispose()
    $source.Dispose()
}
