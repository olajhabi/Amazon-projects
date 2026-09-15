# setup.ps1 - Downloads whisper.cpp pre-built binary and ggml-small model
# Run once before launching VoiceFlow: Right-click → Run with PowerShell

$ErrorActionPreference = "Stop"

$AppDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$WhisperDir = Join-Path $AppDir "whisper"

# URLs - whisper.cpp releases: https://github.com/ggerganov/whisper.cpp/releases
# Grab the latest cublas or CPU build. We use the CPU build (no GPU required).
$WhisperRelease = "b5130"
$WhisperZipUrl  = "https://github.com/ggml-org/whisper.cpp/releases/download/$WhisperRelease/whisper-bin-Win32.zip"
$ModelUrl       = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"

$WhisperZip     = Join-Path $env:TEMP "whisper.zip"
$WhisperExe     = Join-Path $WhisperDir "whisper-cli.exe"
$ModelFile      = Join-Path $WhisperDir "ggml-small.bin"

Write-Host "VoiceFlow setup" -ForegroundColor Cyan
Write-Host "App directory : $AppDir"
Write-Host "Whisper dir   : $WhisperDir"
Write-Host ""

# 1. Create whisper directory
if (-not (Test-Path $WhisperDir)) {
    New-Item -ItemType Directory -Path $WhisperDir | Out-Null
    Write-Host "[+] Created $WhisperDir"
}

# 2. Download whisper.cpp binary
if (-not (Test-Path $WhisperExe)) {
    Write-Host "[*] Downloading whisper.cpp $WhisperRelease ..."
    Invoke-WebRequest -Uri $WhisperZipUrl -OutFile $WhisperZip -UseBasicParsing
    Write-Host "[*] Extracting ..."
    Expand-Archive -Path $WhisperZip -DestinationPath $WhisperDir -Force
    Remove-Item $WhisperZip

    # The zip may contain a sub-folder; find whisper-cli.exe
    $found = Get-ChildItem -Path $WhisperDir -Recurse -Filter "whisper-cli.exe" | Select-Object -First 1
    if ($null -eq $found) {
        # Some builds use "whisper.exe"
        $found = Get-ChildItem -Path $WhisperDir -Recurse -Filter "whisper.exe" | Select-Object -First 1
        if ($null -ne $found) {
            Copy-Item $found.FullName (Join-Path $WhisperDir "whisper-cli.exe")
            Write-Host "[+] Copied whisper.exe as whisper-cli.exe"
        }
    }
    if ($null -eq $found) {
        # Older releases used "main.exe" - try that
        $found = Get-ChildItem -Path $WhisperDir -Recurse -Filter "main.exe" | Select-Object -First 1
        if ($null -ne $found) {
            Copy-Item $found.FullName (Join-Path $WhisperDir "whisper-cli.exe")
            Write-Host "[+] Renamed main.exe to whisper-cli.exe"
        }
    }

    if (-not (Test-Path $WhisperExe)) {
        Write-Host "[!] Could not find whisper-cli.exe in the archive." -ForegroundColor Yellow
        Write-Host "    Contents of $WhisperDir :"
        Get-ChildItem $WhisperDir -Recurse | Select-Object FullName
        Write-Host ""
        Write-Host "    Please copy the whisper executable to:" -ForegroundColor Yellow
        Write-Host "    $WhisperExe" -ForegroundColor Yellow
    } else {
        Write-Host "[+] whisper-cli.exe ready" -ForegroundColor Green
    }
} else {
    Write-Host "[=] whisper-cli.exe already present, skipping download."
}

# 3. Download ggml-small model (~150 MB)
if (-not (Test-Path $ModelFile)) {
    Write-Host "[*] Downloading ggml-small.bin (~150 MB) ..."
    Invoke-WebRequest -Uri $ModelUrl -OutFile $ModelFile -UseBasicParsing
    Write-Host "[+] Model downloaded" -ForegroundColor Green
} else {
    Write-Host "[=] ggml-small.bin already present, skipping download."
}

Write-Host ""
Write-Host "Setup complete!" -ForegroundColor Green
Write-Host "Run VoiceFlow.exe - then hold Right Ctrl to dictate into any app."
