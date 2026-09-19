# Render the existing header waveform as app artwork; run with Windows PowerShell -STA.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets\icon'))
$waveform = [Windows.Media.Geometry]::Parse('M0,18 L5,18 L9,7 L13,30 L18,0 L23,36 L28,12 L32,23 L37,18')
$green = [Windows.Media.BrushConverter]::new().ConvertFromString('#00E676')

# Transparent canvas with a nearly edge-to-edge green mark, legible at tray sizes.
function Get-IconPng([int] $size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 64.0, $size / 64.0))
    $drawing.PushTransform([Windows.Media.TranslateTransform]::new(6.1, 6.8))
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new(1.4, 1.4))
    $pen = [Windows.Media.Pen]::new($green, 3.2)
    $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
    $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
    $drawing.DrawGeometry($null, $pen, $waveform)
    $drawing.Pop(); $drawing.Pop(); $drawing.Pop()
    $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}

[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'wolfeq.png'), (Get-IconPng 256))
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'wolfeq-source.png'), (Get-IconPng 1024))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Get-IconPng $_) })
$file = [IO.File]::Create((Join-Path $assetDirectory 'wolfeq.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
