# Renders the canonical SVG assets into app/tray ICO files and installer PNGs.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assetRoot = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $assetRoot | Out-Null
function Draw-Mark($context, [double]$x, [double]$y, [double]$size, [xml]$svg) {
    $context.PushTransform([Windows.Media.TranslateTransform]::new($x,$y))
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/[double]$svg.svg.width,$size/[double]$svg.svg.height))
    foreach ($shape in $svg.svg.ChildNodes) {
        if ($shape.LocalName -notin @('rect','path')) { continue }
        $fill = if ($shape.GetAttribute('fill') -and $shape.GetAttribute('fill') -ne 'none') { [Windows.Media.BrushConverter]::new().ConvertFromString($shape.GetAttribute('fill')) } else { $null }
        $pen = $null
        if ($shape.GetAttribute('stroke')) {
            $pen = [Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString($shape.GetAttribute('stroke')), [double]$shape.GetAttribute('stroke-width'))
            $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        }
        if ($shape.LocalName -eq 'rect') {
            $context.DrawRoundedRectangle($fill,$pen,[Windows.Rect]::new([double]$shape.x,[double]$shape.y,[double]$shape.width,[double]$shape.height),[double]$shape.rx,[double]$shape.rx)
        } else { $context.DrawGeometry($fill,$pen,[Windows.Media.Geometry]::Parse($shape.d)) }
    }
    $context.Pop(); $context.Pop()
}
function Render-Png([int]$width,[int]$height,[bool]$panel,[string]$source = 'SnappySnap') {
    $svg = [xml](Get-Content -LiteralPath (Join-Path $assetRoot ($source + '.svg')) -Raw)
    $visual = [Windows.Media.DrawingVisual]::new(); $dc = $visual.RenderOpen()
    if ($panel) {
        $dc.DrawRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#202425'),$null,[Windows.Rect]::new(0,0,$width,$height))
        Draw-Mark $dc (($width-160)/2) (($height-160)/2) 160 $svg
    } else { Draw-Mark $dc 0 0 $width $svg }
    $dc.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new(); $encoder.Save($stream); $bytes = $stream.ToArray(); $stream.Dispose()
    return ,$bytes
}
$sizes = @(16,20,24,32,40,48,64,128,256)
foreach ($source in @('SnappySnap','TrayDark','TrayLight')) {
$frames = @($sizes | ForEach-Object { ,(Render-Png $_ $_ $false $source) })
$output = [IO.File]::Create((Join-Path $assetRoot ($source + '.ico'))); $writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
}
[IO.File]::WriteAllBytes((Join-Path $assetRoot 'wizard.png'),(Render-Png 492 942 $true))
[IO.File]::WriteAllBytes((Join-Path $assetRoot 'mark.png'),(Render-Png 128 128 $false))
