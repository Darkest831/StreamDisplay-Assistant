# ==============================================================================
# Script di Compilazione, Firma Hash e Packaging per StreamDisplay Assistant
# Genera l'eseguibile Standalone Single-File (.exe) e l'archivio ZIP per la distribuzione
# ==============================================================================

$ErrorActionPreference = "Stop"

$ProjectDir = "$PSScriptRoot\StreamDisplayAssistant"
if (-not (Test-Path $ProjectDir)) {
    $ProjectDir = $PSScriptRoot
}

$OutputDir = "$PSScriptRoot\Distribuzione_v1.0.0"
$ZipFile = "$PSScriptRoot\StreamDisplayAssistant_v1.0.0.zip"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   STREAMDISPLAY ASSISTANT - BUILD & PACKAGING SYSTEM     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Compilazione Release Standalone Single-File
Write-Host "`n[1/4] Compilazione in corso (.NET 8 Win-x64 Standalone Single-File)..." -ForegroundColor Yellow
dotnet publish "$ProjectDir\StreamDisplayAssistant.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "La compilazione ha riportato errori."
    exit $LASTEXITCODE
}

$PublishFolder = "$ProjectDir\bin\Release\net8.0-windows\win-x64\publish"
$SourceExe = "$PublishFolder\StreamDisplayAssistant.exe"

if (-not (Test-Path $SourceExe)) {
    Write-Error "Eseguibile non trovato in: $SourceExe"
    exit 1
}

# 2. Creazione cartella di distribuzione
Write-Host "`n[2/4] Preparazione cartella di distribuzione..." -ForegroundColor Yellow
Get-Process StreamDisplayAssistant -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

Copy-Item $SourceExe -Destination "$OutputDir\StreamDisplayAssistant.exe"
Copy-Item "$PSScriptRoot\Leggimi.txt" -Destination "$OutputDir\Leggimi.txt" -ErrorAction SilentlyContinue
Copy-Item "$PSScriptRoot\README.txt" -Destination "$OutputDir\README.txt" -ErrorAction SilentlyContinue

# Copia le DLL native WPF se presenti a lato (garantisce compatibilità totale su qualsiasi versione Windows 10/11)
Get-ChildItem -Path $PublishFolder -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination "$OutputDir\$($_.Name)"
}

# 3. Calcolo Hash SHA256 per VirusTotal e GitHub Releases
Write-Host "`n[3/4] Calcolo impronta crittografica SHA256 (per VirusTotal e note di rilascio)..." -ForegroundColor Yellow
$FileHash = (Get-FileHash "$OutputDir\StreamDisplayAssistant.exe" -Algorithm SHA256).Hash
Write-Host ">> SHA256: $FileHash" -ForegroundColor Green
$FileHash | Out-File "$OutputDir\SHA256_CHECKSUM.txt" -Encoding UTF8

# 4. Creazione archivio ZIP pronto per GitHub Releases e Itch.io
Write-Host "`n[4/4] Creazione archivio ZIP di distribuzione..." -ForegroundColor Yellow
if (Test-Path $ZipFile) {
    Remove-Item -Force $ZipFile
}
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -CompressionLevel Optimal

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   PACKAGING COMPLETATO CON SUCCESSO!                     " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Cartella distribuibile: $OutputDir" -ForegroundColor White
Write-Host "File ZIP da caricare : $ZipFile" -ForegroundColor White
Write-Host "Impronta SHA256       : $FileHash" -ForegroundColor White
Write-Host "Passo successivo      : Carica StreamDisplayAssistant.exe su https://www.virustotal.com" -ForegroundColor Cyan
