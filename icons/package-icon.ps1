$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskSource = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'Immersion.png'))
$taskFrames = @()
foreach ($taskSize in @(16,24,32,48,64,128,256)) {
    $taskFrame = [Drawing.Bitmap]::new($taskSize,$taskSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskFrame)
    $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $taskGraphics.Clear([Drawing.Color]::Transparent)
    $taskGraphics.DrawImage($taskSource,[Drawing.Rectangle]::new(0,0,$taskSize,$taskSize))
    $taskBuffer = [IO.MemoryStream]::new()
    $taskFrame.Save($taskBuffer,[Drawing.Imaging.ImageFormat]::Png)
    $taskFrames += @{ Size=$taskSize; Bytes=$taskBuffer.ToArray() }
    $taskGraphics.Dispose(); $taskFrame.Dispose(); $taskBuffer.Dispose()
}
$taskSource.Dispose()
$taskStream = [IO.File]::Create((Join-Path $PSScriptRoot 'Immersion.ico'))
$taskWriter = [IO.BinaryWriter]::new($taskStream)
$taskWriter.Write([UInt16]0); $taskWriter.Write([UInt16]1); $taskWriter.Write([UInt16]$taskFrames.Count)
$taskOffset = 6+16*$taskFrames.Count
foreach ($taskFrame in $taskFrames) {
    $taskDimension = if ($taskFrame.Size -eq 256) { 0 } else { $taskFrame.Size }
    $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
    $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
    $taskWriter.Write([UInt16]1); $taskWriter.Write([UInt16]32)
    $taskWriter.Write([UInt32]$taskFrame.Bytes.Length); $taskWriter.Write([UInt32]$taskOffset)
    $taskOffset += $taskFrame.Bytes.Length
}
foreach ($taskFrame in $taskFrames) { $taskWriter.Write([byte[]]$taskFrame.Bytes) }
$taskWriter.Dispose()
