$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
# Render the new original artwork through the same factory used by the app.
Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'BrandArtwork.cs') -ReferencedAssemblies 'PresentationCore','PresentationFramework','WindowsBase'
$source = [System.Windows.Media.Imaging.BitmapImage]::new()
$source.BeginInit(); $source.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$source.UriSource = [Uri](Join-Path $PSScriptRoot 'yoru-original.png')
$source.EndInit(); $source.Freeze()
$mark = [Yoru.BrandArtwork]::CreateSmall($source)
$canvas = $mark.Drawing
function Render-Icon([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual,[System.Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
    $drawing.DrawDrawing($canvas)
    $drawing.Pop(); $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Render-Icon $_) })
$target = Join-Path $PSScriptRoot 'yoru-v9.ico'
$writer = New-Object System.IO.BinaryWriter([System.IO.File]::Create($target))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'yoru-icon.png'), (Render-Icon 256))
Write-Output "Created $target with $($sizes.Count) icon sizes."

foreach ($size in $sizes) {
    [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot ("yoru-{0}.png" -f $size)), (Render-Icon $size))
}

$manifest = [ordered]@{
    version = '0.9.0'
    originalSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'yoru-original.png') -Algorithm SHA256).Hash.ToLowerInvariant()
    artworkFactorySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'BrandArtwork.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
    iconSha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    sizes = $sizes
    rendering = 'Original Yoru2 portrait crop, downsampled independently with high-quality sampling; no lettering'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'branding-assets.json') -Encoding UTF8
