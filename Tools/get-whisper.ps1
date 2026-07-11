<#
PowerShell helper to download a prebuilt whisper.cpp executable (Windows) and a ggml model.

Usage:
  .\tools\get-whisper.ps1 [-WhisperDir './whisper'] [-ModelDir './models'] [-ModelName 'ggml-base.bin'] [-ModelUrl <url>]

Notes:
- This script attempts to find the latest release of ggerganov/whisper.cpp via the GitHub API
  and download a Windows asset (zip or exe). It then extracts the zip (if necessary) into
  the whisper directory.
- It will also download a model file (default: ggml-base.bin) from a recommended Hugging Face
  URL if one is not supplied.
- You may need to run PowerShell as Administrator to write into certain directories.
#>

param(
    [string]$WhisperDir = "./whisper",
    [string]$ModelDir = "./models",
    [string]$ModelName = "ggml-base.bin",
    [string]$ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/models/ggml-base.bin"
)

function Write-Info($m) { Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Write-ErrorLog($m) { Write-Host "[ERROR] $m" -ForegroundColor Red }

Try {
    Write-Info "Creating folders: $WhisperDir and $ModelDir"
    New-Item -ItemType Directory -Path $WhisperDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null

    # Query GitHub releases for whisper.cpp
    $apiUrl = 'https://api.github.com/repos/ggerganov/whisper.cpp/releases/latest'
    Write-Info "Querying GitHub releases: $apiUrl"
    $resp = Invoke-RestMethod -Uri $apiUrl -UseBasicParsing -Headers @{ 'User-Agent' = 'VoiceType-Get-Whisper-Script' }

    $asset = $null
    foreach ($a in $resp.assets) {
        $name = $a.name.ToLower()
        if ($name -like '*win*' -or $name -like '*.exe' -or $name -like '*.zip') {
            $asset = $a
            break
        }
    }

    if ($null -eq $asset) {
        Write-ErrorLog "No suitable Windows asset found in the latest release."
        Write-ErrorLog "You may need to download a whisper.exe manually and place it in $WhisperDir"
    }
    else {
        $assetUrl = $asset.browser_download_url
        $assetName = $asset.name
        $outPath = Join-Path -Path $WhisperDir -ChildPath $assetName
        Write-Info "Found asset: $assetName"
        Write-Info "Downloading to: $outPath"
        Invoke-WebRequest -Uri $assetUrl -OutFile $outPath -UseBasicParsing

        if ($outPath.ToLower().EndsWith('.zip')) {
            Write-Info "Extracting zip to $WhisperDir"
            Expand-Archive -Path $outPath -DestinationPath $WhisperDir -Force
            Write-Info "Removing downloaded zip"
            Remove-Item $outPath -Force
        }
        else {
            # Ensure executable has .exe extension and is placed directly in $WhisperDir
            Write-Info "Downloaded executable saved to $outPath"
        }
    }

    # Download model if not present
    $modelPath = Join-Path -Path $ModelDir -ChildPath $ModelName
    if (-Not (Test-Path $modelPath)) {
        Write-Info "Downloading model to $modelPath"
        Write-Info "Model URL: $ModelUrl"
        try {
            Invoke-WebRequest -Uri $ModelUrl -OutFile $modelPath -UseBasicParsing -Verbose
        }
        catch {
            Write-ErrorLog "Failed to download model from $ModelUrl"
            Write-ErrorLog "Please download a ggml model (e.g. ggml-base.bin) and place it at: $modelPath"
        }
    }
    else {
        Write-Info "Model already exists at $modelPath"
    }

    Write-Info "Done. Please update appsettings.json if necessary to point to the whisper executable and model paths."
}
Catch {
    Write-ErrorLog "Unexpected error: $_"
}
