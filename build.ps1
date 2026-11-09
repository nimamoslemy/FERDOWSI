param(
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
Set-Location $ProjectRoot

$totalSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "=== Building FERDOWSI (C++20 + Python 3 + C# WPF + Google Gemini + Phi Silica NPU) ===" -ForegroundColor Cyan

# Stop running FERDOWSI / grammerV instances if rebuilding
Get-Process FERDOWSI, grammerV -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Security hygiene: migrate any local settings.json to %APPDATA%\FERDOWSI and remove local secrets / pycache
$localSettings = Join-Path $ProjectRoot "settings.json"
if (Test-Path $localSettings) {
    $appDataFerdowsi = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "FERDOWSI"
    New-Item -ItemType Directory -Force -Path $appDataFerdowsi | Out-Null
    $targetSettings = Join-Path $appDataFerdowsi "settings.json"
    if (-not (Test-Path $targetSettings)) {
        Copy-Item $localSettings $targetSettings -Force
    }
    Remove-Item $localSettings -Force
    Write-Host "[Security] Migrated personal settings.json to %APPDATA%\FERDOWSI and scrubbed project folder." -ForegroundColor Yellow
}
Get-ChildItem -Path $ProjectRoot -Directory -Recurse -Filter "__pycache__" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
foreach ($legacyFile in @("grammerV.exe", "grammerV.ico", "grammerV.png")) {
    $lf = Join-Path $ProjectRoot $legacyFile
    if (Test-Path $lf) { Remove-Item $lf -Force -ErrorAction SilentlyContinue }
}

# 1. Generate FERDOWSI.ico and FERDOWSI.png assets
$stepSw = [System.Diagnostics.Stopwatch]::StartNew()
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(64, 64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 64, 64)),
    [System.Drawing.Color]::FromArgb(255, 16, 185, 129),
    [System.Drawing.Color]::FromArgb(255, 5, 150, 105),
    45.0
)
$g.FillEllipse($brush, 3, 3, 58, 58)
$font = New-Object System.Drawing.Font("Segoe UI", 33, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString("F", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, -1, 64, 64)), $sf)
$g.Dispose()

$pngPath = Join-Path $ProjectRoot "FERDOWSI.png"
$icoPath = Join-Path $ProjectRoot "FERDOWSI.ico"
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Write valid single-image PNG-encoded ICO file
$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
$bw.Write([byte]64); $bw.Write([byte]64); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$pngBytes.Length); $bw.Write([uint32]22)
$bw.Write($pngBytes)
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose(); $bmp.Dispose()
Write-Host ("[1/4] Generated FERDOWSI branding icons in {0} ms" -f $stepSw.ElapsedMilliseconds) -ForegroundColor Gray

# 2. Compile C++20 Native Hook, Caret, Spell & Phi Silica WinRT DLL (stripped with -s so no build paths/symbols leak)
$stepSw.Restart()
$gpp = "C:\Strawberry\c\bin\g++.exe"
if (-not (Test-Path $gpp)) {
    $gppCmd = Get-Command g++ -ErrorAction SilentlyContinue
    if ($gppCmd) { $gpp = $gppCmd.Source } else { throw "g++.exe not found!" }
}
$dllOut = Join-Path $ProjectRoot "grammerv_native.dll"
$cppSrc = Join-Path $ProjectRoot "src\cpp\grammerv_native.cpp"
$rcSrc  = Join-Path $ProjectRoot "src\cpp\grammerv_version.rc"
$rcObj  = Join-Path ([System.IO.Path]::GetTempPath()) "grammerv_version.o"
$windres = Join-Path (Split-Path -Parent $gpp) "windres.exe"
if ((Test-Path $windres) -and (Test-Path $rcSrc)) {
    & $windres -i $rcSrc -o $rcObj
    & $gpp -O3 -s -std=c++20 -shared -static -static-libgcc -static-libstdc++ `
        -o $dllOut $cppSrc $rcObj `
        -luser32 -lgdi32 -lole32 -loleaut32 -loleacc -luuid -lwinmm
    Remove-Item $rcObj -Force -ErrorAction SilentlyContinue
} else {
    & $gpp -O3 -s -std=c++20 -shared -static -static-libgcc -static-libstdc++ `
        -o $dllOut $cppSrc `
        -luser32 -lgdi32 -lole32 -loleaut32 -loleacc -luuid -lwinmm
}
if ($LASTEXITCODE -ne 0) { throw "C++ DLL compilation failed!" }
Write-Host ("[2/4] Compiled stripped C++20 grammerv_native.dll in {0} ms" -f $stepSw.ElapsedMilliseconds) -ForegroundColor Green

# 3. Stage Python NLP Engine
$stepSw.Restart()
$engineDir = Join-Path $ProjectRoot "engine"
New-Item -ItemType Directory -Force -Path $engineDir | Out-Null
$pySrc = Join-Path $ProjectRoot "src\python\nlp_engine.py"
$pyDst = Join-Path $engineDir "nlp_engine.py"
Copy-Item $pySrc $pyDst -Force
Write-Host ("[3/4] Staged Python NLP engine in {0} ms" -f $stepSw.ElapsedMilliseconds) -ForegroundColor Gray

# 4. Compile 64-bit C# WPF/UIA Standalone Executable (embedding C++ DLL + Python engine + Flag PNG, /debug- for zero PDB path leak)
$stepSw.Restart()
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
$exeOut = Join-Path $ProjectRoot "FERDOWSI.exe"
$flagAsset = Join-Path $ProjectRoot "assets\ferdowsi_flag.png"
if (-not (Test-Path $flagAsset)) { throw "Missing brand flag asset: $flagAsset" }

$csFiles = @(
    (Join-Path $ProjectRoot "src\csharp\NativeBridge.cs"),
    (Join-Path $ProjectRoot "src\csharp\PythonEngineClient.cs"),
    (Join-Path $ProjectRoot "src\csharp\AppIconGenerator.cs"),
    (Join-Path $ProjectRoot "src\csharp\FloatingOverlay.cs"),
    (Join-Path $ProjectRoot "src\csharp\StudioWindow.cs"),
    (Join-Path $ProjectRoot "src\csharp\Program.cs")
)

$appManifest = Join-Path $ProjectRoot "src\csharp\app.manifest"
$dllGz = Join-Path ([System.IO.Path]::GetTempPath()) "grammerv_native.dll.gz"
$inBytes = [System.IO.File]::ReadAllBytes($dllOut)
$fsGz = [System.IO.File]::Create($dllGz)
$gzStream = New-Object System.IO.Compression.GZipStream($fsGz, [System.IO.Compression.CompressionMode]::Compress)
$gzStream.Write($inBytes, 0, $inBytes.Length)
$gzStream.Close()
$fsGz.Close()

& $csc /nologo /codepage:65001 /utf8output /debug- /optimize+ /target:winexe /platform:x64 `
    /win32icon:$icoPath `
    /win32manifest:$appManifest `
    /out:$exeOut `
    /resource:"$dllGz,grammerv_native.dll.gz" `
    /resource:"$pyDst,nlp_engine.py" `
    /resource:"$flagAsset,ferdowsi_flag.png" `
    /r:"$fw\System.dll" `
    /r:"$fw\System.Core.dll" `
    /r:"$fw\System.Drawing.dll" `
    /r:"$fw\System.Windows.Forms.dll" `
    /r:"$fw\System.Web.Extensions.dll" `
    /r:"$fw\System.Xaml.dll" `
    /r:"$fw\WPF\WindowsBase.dll" `
    /r:"$fw\WPF\PresentationCore.dll" `
    /r:"$fw\WPF\PresentationFramework.dll" `
    /r:"$fw\WPF\UIAutomationClient.dll" `
    /r:"$fw\WPF\UIAutomationTypes.dll" `
    $csFiles
Remove-Item $dllGz -Force -ErrorAction SilentlyContinue

if ($LASTEXITCODE -ne 0) { throw "C# FERDOWSI.exe compilation failed!" }
Write-Host ("[4/4] Compiled 64-bit standalone FERDOWSI.exe in {0} ms" -f $stepSw.ElapsedMilliseconds) -ForegroundColor Green

# Run self-test to generate fresh FERDOWSI_Help_Guide.pdf and verify build integrity
$selfTestReport = Join-Path ([System.IO.Path]::GetTempPath()) "ferdowsi_build_selftest.txt"
& $exeOut "--self-test" $selfTestReport
if ($LASTEXITCODE -ne 0) {
    if (Test-Path $selfTestReport) { Get-Content $selfTestReport | Write-Host -ForegroundColor Red }
    throw "FERDOWSI.exe --self-test failed!"
}
$pdfPath = Join-Path $ProjectRoot "FERDOWSI_Help_Guide.pdf"

# Sync installed copy in %LOCALAPPDATA%\Programs\FERDOWSI and %LOCALAPPDATA%\FERDOWSI\runtime if present
$installedDir = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Programs\FERDOWSI"
if (Test-Path $installedDir) {
    New-Item -ItemType Directory -Force -Path (Join-Path $installedDir "engine") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $installedDir "assets") | Out-Null
    Copy-Item $exeOut    (Join-Path $installedDir "FERDOWSI.exe") -Force
    Copy-Item $dllOut    (Join-Path $installedDir "grammerv_native.dll") -Force
    Copy-Item $pyDst     (Join-Path $installedDir "engine\nlp_engine.py") -Force
    Copy-Item $flagAsset (Join-Path $installedDir "assets\ferdowsi_flag.png") -Force
    Copy-Item $icoPath   (Join-Path $installedDir "FERDOWSI.ico") -Force
    Copy-Item $pngPath   (Join-Path $installedDir "FERDOWSI.png") -Force
    if (Test-Path $pdfPath) { Copy-Item $pdfPath (Join-Path $installedDir "FERDOWSI_Help_Guide.pdf") -Force }
    Write-Host "[Sync] Updated installed copy at $installedDir" -ForegroundColor Green
}
$runtimeCacheDir = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "FERDOWSI\runtime"
if (Test-Path $runtimeCacheDir) {
    Copy-Item $dllOut (Join-Path $runtimeCacheDir "grammerv_native.dll") -Force -ErrorAction SilentlyContinue
    Copy-Item $pyDst  (Join-Path $runtimeCacheDir "nlp_engine.py") -Force -ErrorAction SilentlyContinue
}

$totalSw.Stop()
$exeSizeKb = [math]::Round((Get-Item $exeOut).Length / 1KB, 1)
Write-Host ("=== BUILD SUCCEEDED in {0} ms ({1} KB) -> {2} ===" -f $totalSw.ElapsedMilliseconds, $exeSizeKb, $exeOut) -ForegroundColor Cyan
