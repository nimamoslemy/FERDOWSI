param(
    [string]$ProjectRoot = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Split-Path -Parent $ProjectRoot) "FERDOWSI_Installer"
}

Write-Host "=== Packaging Clean Publish-Ready FERDOWSI v2.0.0 Installer ===" -ForegroundColor Cyan

# Ensure fresh clean build first
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectRoot "build.ps1")
if ($LASTEXITCODE -ne 0) { throw "Build failed before packaging!" }

# Prepare clean output directory
Get-Process "FERDOWSI_Setup*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $OutputDir) {
    Get-ChildItem -Path $OutputDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Verify NO settings.json exists in ProjectRoot
if (Test-Path (Join-Path $ProjectRoot "settings.json")) {
    throw "SECURITY ABORT: settings.json found in project root!"
}

$exeFile      = Join-Path $ProjectRoot "FERDOWSI.exe"
$dllFile      = Join-Path $ProjectRoot "grammerv_native.dll"
$pyFile       = Join-Path $ProjectRoot "engine\nlp_engine.py"
$flagFile     = Join-Path $ProjectRoot "assets\ferdowsi_flag.png"
$icoFile      = Join-Path $ProjectRoot "FERDOWSI.ico"
$pngFile      = Join-Path $ProjectRoot "FERDOWSI.png"
$manifestFile = Join-Path $ProjectRoot "AppxManifest.xml"
$phiScript    = Join-Path $ProjectRoot "enable_phi_silica.ps1"
$readmeFile   = Join-Path $ProjectRoot "README.md"
$licenseFile  = Join-Path $ProjectRoot "LICENSE.txt"
$setupCs      = Join-Path $ProjectRoot "src\installer\FerdowsiSetup.cs"

# 1. Compile Native 64-bit Windows Setup Wizard (FERDOWSI_Setup_v2.0.0.exe) with asInvoker Manifest & GZip Payload (zero PE-in-PE heuristic popups)
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
$setupOut = Join-Path $OutputDir "FERDOWSI_Setup_v2.0.0.exe"
$appManifest = Join-Path $ProjectRoot "src\csharp\app.manifest"

function Compress-ToGzipTemp([string]$srcFile, [string]$tempName) {
    $outGz = Join-Path ([System.IO.Path]::GetTempPath()) $tempName
    $bytes = [System.IO.File]::ReadAllBytes($srcFile)
    $fs = [System.IO.File]::Create($outGz)
    $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionMode]::Compress)
    $gz.Write($bytes, 0, $bytes.Length)
    $gz.Close()
    $fs.Close()
    return $outGz
}

$exeGz = Compress-ToGzipTemp $exeFile "payload_FERDOWSI.exe.gz"
$dllGz = Compress-ToGzipTemp $dllFile "payload_grammerv_native.dll.gz"

& $csc /nologo /codepage:65001 /utf8output /debug- /optimize+ /target:winexe /platform:x64 `
    /win32icon:$icoFile `
    /win32manifest:$appManifest `
    /out:$setupOut `
    /resource:"$exeGz,payload_FERDOWSI.exe.gz" `
    /resource:"$dllGz,payload_grammerv_native.dll.gz" `
    /resource:"$pyFile,payload_nlp_engine.py" `
    /resource:"$flagFile,payload_ferdowsi_flag.png" `
    /resource:"$icoFile,payload_FERDOWSI.ico" `
    /resource:"$pngFile,payload_FERDOWSI.png" `
    /resource:"$manifestFile,payload_AppxManifest.xml" `
    /resource:"$phiScript,payload_enable_phi_silica.ps1" `
    /resource:"$readmeFile,payload_README.md" `
    /resource:"$licenseFile,payload_LICENSE.txt" `
    /r:"$fw\System.dll" `
    /r:"$fw\System.Core.dll" `
    /r:"$fw\System.Drawing.dll" `
    /r:"$fw\System.Windows.Forms.dll" `
    /r:"$fw\System.Xaml.dll" `
    /r:"$fw\WPF\WindowsBase.dll" `
    /r:"$fw\WPF\PresentationCore.dll" `
    /r:"$fw\WPF\PresentationFramework.dll" `
    $setupCs
Remove-Item $exeGz, $dllGz -Force -ErrorAction SilentlyContinue

if ($LASTEXITCODE -ne 0) { throw "Failed to compile FERDOWSI_Setup_v2.0.0.exe!" }

# Verify embedded installer resources
$verifyProc = Start-Process -FilePath $setupOut -ArgumentList "--verify-package" -Wait -PassThru -NoNewWindow
if ($verifyProc.ExitCode -ne 0) { throw "Installer payload verification failed (exit code $($verifyProc.ExitCode))!" }
Write-Host "[1/3] Built & verified GUI Installer: $setupOut" -ForegroundColor Green

# 2. Build Clean Portable ZIP Archive (FERDOWSI_Portable_v2.0.0_x64.zip)
$stageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ferdowsi_stage_" + [Guid]::NewGuid().ToString("N"))
$appFolder = Join-Path $stageDir "FERDOWSI"
New-Item -ItemType Directory -Force -Path $appFolder | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $appFolder "engine") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $appFolder "assets") | Out-Null

Copy-Item $exeFile      (Join-Path $appFolder "FERDOWSI.exe") -Force
Copy-Item $dllFile      (Join-Path $appFolder "grammerv_native.dll") -Force
Copy-Item $pyFile       (Join-Path $appFolder "engine\nlp_engine.py") -Force
Copy-Item $flagFile     (Join-Path $appFolder "assets\ferdowsi_flag.png") -Force
Copy-Item $icoFile      (Join-Path $appFolder "FERDOWSI.ico") -Force
Copy-Item $pngFile      (Join-Path $appFolder "FERDOWSI.png") -Force
Copy-Item $manifestFile (Join-Path $appFolder "AppxManifest.xml") -Force
Copy-Item $phiScript    (Join-Path $appFolder "enable_phi_silica.ps1") -Force
Copy-Item $readmeFile   (Join-Path $appFolder "README.md") -Force
Copy-Item $licenseFile  (Join-Path $appFolder "LICENSE.txt") -Force

$launchBat = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -Command `"Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File -ErrorAction SilentlyContinue`"`r`nstart `"`" `"%~dp0FERDOWSI.exe`"`r`n"
[System.IO.File]::WriteAllText((Join-Path $appFolder "Launch_FERDOWSI.bat"), $launchBat, [System.Text.Encoding]::ASCII)

$zipOut = Join-Path $OutputDir "FERDOWSI_Portable_v2.0.0_x64.zip"
Compress-Archive -Path $appFolder -DestinationPath $zipOut -CompressionLevel Optimal -Force
Remove-Item -Path $stageDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "[2/3] Built clean Portable ZIP: $zipOut" -ForegroundColor Green

# 3. Copy License, Write Bilingual Release Guide & Generate SHA256 Checksums
Copy-Item $licenseFile (Join-Path $OutputDir "LICENSE.txt") -Force

$installGuide = @"
# FERDOWSI v2 (v2.0.0) — Official Installation Package (Windows 10 / 11 x64)

GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)

---

## Included Release Files
1. **`FERDOWSI_Setup_v2.0.0.exe`** — Interactive Windows Setup Wizard (creates Desktop & Start Menu shortcuts, optional Windows Startup tray launch, and uninstaller in Windows Installed Apps).
2. **`FERDOWSI_Portable_v2.0.0_x64.zip`** — Zero-install Portable Edition (unzip anywhere and run `FERDOWSI.exe`).
3. **`SHA256SUMS.txt`** — SHA-256 integrity checksums for GitHub Releases.
4. **`LICENSE.txt`** — MIT Open-Source License.

---

## Quick Start (English)
1. Run **`FERDOWSI_Setup_v2.0.0.exe`** and click **Install FERDOWSI**.
2. Once **FERDOWSI v2** opens, type anywhere in Windows (Edge, Chrome, Word, Notepad, Telegram, VS Code) or press **`Alt + G`** (`Ctrl + Shift + G`) to summon the assistant right above your cursor.
3. **🎤 Voice Dictation (Groq Whisper & Deepgram) + `Cleanup Voice` vs `Exact Voice`**:
   - Click **`🎤`** on the short floating window to speak. Choose **`Cleanup Voice`** (removes fillers/stutters and fixes grammar while strictly cleaning your speech — never answering or responding to your voice) or **`Exact Voice`** (verbatim transcript).
4. **🔊 Read Aloud (`Gemini`, `Deepgram`, `Voice AI`)**:
   - Click **`🔊`** on the short floating window or expanded card to read text aloud.
5. **Short Window Customization**:
   - You can hide/disable the `🎤`, `Clean/Exact`, or `🔊` buttons on the short cursor window anytime in **FERDOWSI v2 Studio Settings**.
"@
[System.IO.File]::WriteAllText((Join-Path $OutputDir "README_INSTALL.md"), $installGuide, [System.Text.Encoding]::UTF8)

$setupHash = (Get-FileHash -Path $setupOut -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash   = (Get-FileHash -Path $zipOut   -Algorithm SHA256).Hash.ToLowerInvariant()
$sumsText  = "$setupHash  FERDOWSI_Setup_v2.0.0.exe`r`n$zipHash  FERDOWSI_Portable_v2.0.0_x64.zip`r`n"
[System.IO.File]::WriteAllText((Join-Path $OutputDir "SHA256SUMS.txt"), $sumsText, [System.Text.Encoding]::ASCII)

Write-Host "[3/3] Generated README_INSTALL.md, LICENSE.txt, and SHA256SUMS.txt" -ForegroundColor Green
Write-Host ("=== PACKAGE READY -> {0} ===" -f $OutputDir) -ForegroundColor Cyan
