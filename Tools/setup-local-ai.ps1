<#
.SYNOPSIS
  Downloads the local AI stack the game talks to into ./LocalAI (gitignored):
    - llama.cpp server (Vulkan build)   -> chat LLM (OpenAI-compatible API)
    - Qwen3.5 GGUF model                -> strong at English + Mandarin
    - whisper.cpp server (CPU build)    -> speech-to-text
    - Whisper multilingual model        -> English now, Chinese later
    - Piper TTS + voices                -> text-to-speech (English + Mandarin voices)

  Re-running is safe: finished files are skipped.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/setup-local-ai.ps1
  powershell -ExecutionPolicy Bypass -File Tools/setup-local-ai.ps1 -ModelSize 2B -WhisperModel small-q5_1
#>
param(
    [ValidateSet("2B", "4B", "9B")] [string]$ModelSize = "2B",
    [ValidateSet("base", "small-q5_1", "small", "large-v3-turbo-q5_0")] [string]$WhisperModel = "base"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Join-Path (Split-Path -Parent $PSScriptRoot) "LocalAI"
New-Item -ItemType Directory -Force -Path $root | Out-Null
Write-Host "Installing local AI stack into $root"

function Get-File([string]$url, [string]$dest) {
    if (Test-Path $dest) { Write-Host "  [skip] $(Split-Path -Leaf $dest)"; return }
    Write-Host "  [get ] $url"
    $tmp = "$dest.part"
    & curl.exe -L --fail --retry 5 -C - -o $tmp $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    Move-Item -Force $tmp $dest
}

function Get-LatestAsset([string]$repo, [string]$pattern) {
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=10" -Headers @{ "User-Agent" = "setup-local-ai" }
    foreach ($r in $releases) {
        $a = $r.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
        if ($a) { return $a.browser_download_url }
    }
    throw "No asset matching '$pattern' in $repo"
}

function Expand-Into([string]$zip, [string]$dir, [string]$probe) {
    if (Test-Path (Join-Path $dir $probe)) { Write-Host "  [skip] extract $(Split-Path -Leaf $zip)"; return }
    Write-Host "  [unzip] $(Split-Path -Leaf $zip)"
    $staging = "$dir.staging"
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    Expand-Archive -Force -Path $zip -DestinationPath $staging
    # Flatten: find the folder that actually contains the probe exe.
    $hit = Get-ChildItem -Recurse -Path $staging -Filter $probe | Select-Object -First 1
    if (-not $hit) { throw "$probe not found in $zip" }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -Recurse -Force -Path (Join-Path $hit.DirectoryName "*") -Destination $dir
    Remove-Item -Recurse -Force $staging
}

$dl = Join-Path $root "downloads"
New-Item -ItemType Directory -Force -Path $dl | Out-Null

# --- llama.cpp server -------------------------------------------------------
Write-Host "`n[1/5] llama.cpp server (Vulkan)"
$llamaUrl = Get-LatestAsset "ggml-org/llama.cpp" "^llama-b\d+-bin-win-vulkan-x64\.zip$"
$llamaZip = Join-Path $dl (Split-Path -Leaf $llamaUrl)
Get-File $llamaUrl $llamaZip
Expand-Into $llamaZip (Join-Path $root "llama") "llama-server.exe"

# --- LLM model --------------------------------------------------------------
Write-Host "`n[2/5] Qwen3.5-$ModelSize model"
$models = Join-Path $root "models"
New-Item -ItemType Directory -Force -Path $models | Out-Null
$ggufName = "Qwen3.5-$ModelSize-Q4_K_M.gguf"
Get-File "https://huggingface.co/unsloth/Qwen3.5-$ModelSize-GGUF/resolve/main/$ggufName" (Join-Path $models $ggufName)

# --- whisper.cpp server -----------------------------------------------------
Write-Host "`n[3/5] whisper.cpp server"
$whisperUrl = Get-LatestAsset "ggml-org/whisper.cpp" "^whisper-bin-x64\.zip$"
$whisperZip = Join-Path $dl "whisper-bin-x64.zip"
Get-File $whisperUrl $whisperZip
Expand-Into $whisperZip (Join-Path $root "whisper") "whisper-server.exe"

Write-Host "`n[4/5] Whisper model ($WhisperModel)"
Get-File "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$WhisperModel.bin" (Join-Path $models "ggml-$WhisperModel.bin")

# --- Piper TTS --------------------------------------------------------------
Write-Host "`n[5/5] Piper TTS + voices"
$piperZip = Join-Path $dl "piper_windows_amd64.zip"
Get-File "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip" $piperZip
Expand-Into $piperZip (Join-Path $root "piper") "piper.exe"

$voices = Join-Path $root "voices"
New-Item -ItemType Directory -Force -Path $voices | Out-Null
$voiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main"
foreach ($v in @(
        # Public-domain training data (LibriVox / LJ Speech) so the voices are free to use.
        @{ path = "en/en_US/kristin/medium"; name = "en_US-kristin-medium" },
        @{ path = "en/en_US/ljspeech/medium"; name = "en_US-ljspeech-medium" },
        @{ path = "zh/zh_CN/huayan/medium"; name = "zh_CN-huayan-medium" })) {
    Get-File "$voiceBase/$($v.path)/$($v.name).onnx" (Join-Path $voices "$($v.name).onnx")
    Get-File "$voiceBase/$($v.path)/$($v.name).onnx.json" (Join-Path $voices "$($v.name).onnx.json")
}

# Record which model files were chosen so the game can find them.
@{
    llmModel     = "models/$ggufName"
    whisperModel = "models/ggml-$WhisperModel.bin"
    voice        = "voices/en_US-kristin-medium.onnx"
} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root "localai.json")

Write-Host "`nDone. Local AI stack is ready in $root"
