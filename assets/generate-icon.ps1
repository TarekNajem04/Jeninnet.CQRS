#
# Copyright (c) Jeninnet.
# Part of the Jeninnet Platform.
# Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
# Licensed under the MIT License.
#
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
}
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $arc = [System.Drawing.RectangleF]::new($Rectangle.X, $Rectangle.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rectangle.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rectangle.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rectangle.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Icon {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 512.0
    $graphics.ScaleTransform($scale, $scale)

    $background = New-RoundedPath ([System.Drawing.RectangleF]::new(0, 0, 512, 512)) 112
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 0),
        [System.Drawing.PointF]::new(512, 512),
        [System.Drawing.ColorTranslator]::FromHtml('#0B1220'),
        [System.Drawing.ColorTranslator]::FromHtml('#1B2B4A'))
    $graphics.FillPath($brush, $background)

    $ringPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#1E3A5F'), 2)
    $graphics.DrawEllipse($ringPen, 106, 106, 300, 300)

    $arcRect = [System.Drawing.RectangleF]::new(140, 140, 232, 232)
    $glowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(55, 34, 211, 238), 46)
    $glowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $glowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($glowPen, $arcRect, 180, 180)

    $commandPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#38BDF8'), 30)
    $commandPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $commandPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($commandPen, $arcRect, 180, 180)
    $commandArrow = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(372, 256),
        [System.Drawing.PointF]::new(342, 218),
        [System.Drawing.PointF]::new(402, 218)
    )
    $commandBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#38BDF8'))
    $graphics.FillPolygon($commandBrush, $commandArrow)

    $queryGlowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(55, 167, 139, 250), 46)
    $queryGlowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $queryGlowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($queryGlowPen, $arcRect, 0, 180)

    $queryPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#A78BFA'), 30)
    $queryPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $queryPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($queryPen, $arcRect, 0, 180)
    $queryArrow = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(140, 256),
        [System.Drawing.PointF]::new(170, 294),
        [System.Drawing.PointF]::new(110, 294)
    )
    $queryBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A78BFA'))
    $graphics.FillPolygon($queryBrush, $queryArrow)

    $nodePath = New-RoundedPath ([System.Drawing.RectangleF]::new(216, 216, 80, 80)) 20
    $nodeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F8FAFC'))
    $graphics.FillPath($nodeBrush, $nodePath)

    $innerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0B1220'))
    $graphics.FillEllipse($innerBrush, 240, 240, 32, 32)
    $coreBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#22D3EE'))
    $graphics.FillEllipse($coreBrush, 248, 248, 16, 16)

    $graphics.Dispose()
    return $bitmap
}

$master = New-Icon -Size 512
$small = New-Icon -Size 128

$masterPath = Join-Path $OutputDirectory 'icon.png'
$smallPath = Join-Path $OutputDirectory 'icon-128.png'
$master.Save($masterPath, [System.Drawing.Imaging.ImageFormat]::Png)
$small.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Png)

$master.Dispose()
$small.Dispose()

Write-Host "Wrote $masterPath and $smallPath"
