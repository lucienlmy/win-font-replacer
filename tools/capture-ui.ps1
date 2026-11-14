# Launch FontSwap and capture its window into a PNG for visual inspection.
param(
    [string]$Out = "assets\screenshot-main.png",
    [int]$Delay = 1200,
    [switch]$KeepOpen
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root "dist\WinFontReplacer.exe"
if (-not (Test-Path $exe)) {
    throw ("Executable not found: " + $exe)
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class Win32Capture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);
}
"@

$process = Start-Process -FilePath $exe -PassThru
$handle = [IntPtr]::Zero
for ($i = 0; $i -lt 80; $i++) {
    Start-Sleep -Milliseconds 200
    $process.Refresh()
    if ($process.HasExited) {
        throw "Program exited immediately after launch."
    }
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
        $handle = $process.MainWindowHandle
        break
    }
}

if ($handle -eq [IntPtr]::Zero) {
    throw "Timed out waiting for the window."
}

[Win32Capture]::ShowWindow($handle, 5) | Out-Null
[Win32Capture]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds $Delay

$rect = New-Object "Win32Capture+RECT"
[Win32Capture]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Invalid window size."
}

$dpi = [Win32Capture]::GetDpiForWindow($handle)
if ($dpi -le 0) {
    $dpi = 96
}
$factor = $dpi / 96.0
$pixelWidth = [int][Math]::Round($width * $factor)
$pixelHeight = [int][Math]::Round($height * $factor)
Write-Host ("Window dpi=" + $dpi + ", logical size " + $width + "x" + $height + ", pixel size " + $pixelWidth + "x" + $pixelHeight)

$bitmap = New-Object System.Drawing.Bitmap $pixelWidth, $pixelHeight
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$captured = [Win32Capture]::PrintWindow($handle, $hdc, 2)
$graphics.ReleaseHdc($hdc)
if (-not $captured) {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
}
$graphics.Dispose()

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host ("Screenshot saved: " + $outPath + "  (" + $width + "x" + $height + ")")

if (-not $KeepOpen) {
    $process.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 300
    if (-not $process.HasExited) {
        $process.Kill()
    }
    Write-Host "Program closed."
} else {
    Write-Host ("Program still running, PID " + $process.Id)
}
