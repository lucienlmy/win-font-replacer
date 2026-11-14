# 编译 WinFontReplacer：C# 5（.NET Framework 4.x 自带 csc），输出单文件 exe 到 dist。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src"
$distDir = Join-Path $root "dist"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "找不到 .NET Framework 编译器 csc.exe。"
}

# 图标沿用 1.x 的 app.ico；只有缺失时才重新生成。
$icon = Join-Path $srcDir "app.ico"
if (-not (Test-Path $icon)) {
    & (Join-Path $root "tools\make-icon.ps1")
}
if (-not (Test-Path $icon)) {
    throw "找不到图标文件：$icon"
}

$sources = Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name | Select-Object -ExpandProperty FullName
if ($sources.Count -eq 0) {
    throw "src 目录下没有找到源文件。"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$exe = Join-Path $distDir "WinFontReplacer.exe"
$legacyExe = Join-Path $distDir "FontSwap.exe"
if (Test-Path $legacyExe) {
    Remove-Item -LiteralPath $legacyExe -Force
}

$arguments = @(
    "/nologo"
    "/target:winexe"
    "/codepage:65001"
    "/utf8output"
    "/platform:anycpu"
    "/optimize+"
    "/warn:4"
    "/out:$exe"
    ("/win32manifest:" + (Join-Path $srcDir "App.manifest"))
    ("/win32icon:" + $icon)
    "/r:System.dll"
    "/r:System.Core.dll"
    "/r:System.Drawing.dll"
    "/r:System.Windows.Forms.dll"
) + $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败（退出码 $LASTEXITCODE）。"
}

Write-Host ""
Write-Host ("编译完成：" + $exe)

$readme = Join-Path $root "README.md"
if (Test-Path $readme) {
    Copy-Item -LiteralPath $readme -Destination (Join-Path $distDir "README.md") -Force
}

# README 里引用的截图，让 dist 目录也能直接看说明。
$assetsDir = Join-Path $root "assets"
if (Test-Path $assetsDir) {
    $distAssets = Join-Path $distDir "assets"
    New-Item -ItemType Directory -Force -Path $distAssets | Out-Null
    Copy-Item -Path (Join-Path $assetsDir "*.png") -Destination $distAssets -Force
}
