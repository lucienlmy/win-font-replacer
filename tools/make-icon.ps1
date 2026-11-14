# 生成 FontSwap 的图标：圆角蓝底 + 白色 "Aa"，输出多尺寸 ICO 与 256px PNG。
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$srcIco = Join-Path $root "src\app.ico"
$assetsPng = Join-Path $root "assets\icon-256.png"

if ((Test-Path $srcIco) -and -not $Force) {
    Write-Host "图标已存在，跳过生成（使用 -Force 可重新生成）。"
    exit 0
}

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

public static class FontSwapIcon
{
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        if (d <= 0f)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    public static void Draw(int size, string path)
    {
        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            RectangleF rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
            float radius = Math.Max(2f, size * 0.22f);
            using (GraphicsPath shape = Rounded(rect, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new PointF(0f, 0f), new PointF(size, size),
                Color.FromArgb(255, 96, 165, 250), Color.FromArgb(255, 29, 78, 216)))
            {
                g.FillPath(brush, shape);
            }

            // 顶部高光
            using (GraphicsPath shine = Rounded(new RectangleF(size * 0.12f, size * 0.10f, size * 0.76f, size * 0.34f), radius * 0.6f))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
            {
                g.FillPath(soft, shine);
            }

            string text = "Aa";
            float fontSize = size * 0.40f;
            using (FontFamily family = new FontFamily("Segoe UI"))
            using (Font font = new Font(family, fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                RectangleF box = new RectangleF(0f, size * 0.02f, size, size);
                g.DrawString(text, font, white, box, format);
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }
}
"@

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.ArrayList

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $assetsPng) | Out-Null

foreach ($size in $sizes) {
    $temp = Join-Path $env:TEMP ("fontswap-icon-" + $size + ".png")
    [FontSwapIcon]::Draw($size, $temp)
    $bytes = [System.IO.File]::ReadAllBytes($temp)
    Remove-Item -LiteralPath $temp -Force
    [void]$frames.Add([pscustomobject]@{ Size = $size; Data = $bytes })
}

# 组装 ICO（内嵌 PNG 数据）
$count = $frames.Count
$headerSize = 6
$entrySize = 16
$offset = $headerSize + $entrySize * $count

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$count)

foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([Byte]$dim)
    $writer.Write([Byte]$dim)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Data)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($srcIco, $stream.ToArray())
$writer.Close()
$stream.Close()

[FontSwapIcon]::Draw(256, $assetsPng)

Write-Host ("已生成 " + $srcIco)
Write-Host ("已生成 " + $assetsPng)
