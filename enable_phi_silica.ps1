$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "=== FERDOWSI — Copilot+ NPU Phi Silica Identity Registration ===" -ForegroundColor Cyan
$manifest = Join-Path $root "AppxManifest.xml"

try {
    Add-AppxPackage -Register $manifest -ExternalLocation $root
    Write-Host "[OK] Sparse MSIX identity (systemAIModels) registered! Restart FERDOWSI.exe to use hardware Phi Silica NPU." -ForegroundColor Green
} catch {
    Write-Host "[INFO] Windows requires Developer Mode enabled to register an unpackaged Sparse MSIX identity." -ForegroundColor Yellow
    Write-Host "Opening Windows Developer Settings (toggle 'Developer Mode' ON, then re-run this script)..." -ForegroundColor Yellow
    Start-Process "ms-settings:developers"
}
