<#
.SYNOPSIS
  Downloads the local AI stack into ./LocalAI (gitignored):
    - llama.cpp server (Vulkan build)           -> runs the chat LLM (OpenAI-compatible API)
    - Qwen3.5 GGUF model                        -> Mei + the shopkeepers (strong Mandarin)
    - sherpa-onnx runtime (in-process via C#)   -> speech recognition + speech synthesis
    - SenseVoice                                -> fast, accurate Mandarin speech recognition
    - Kokoro v1.1-zh (multi-speaker)            -> Mei's natural voice (Mandarin + English)
    - Matcha zh-en + vocos vocoder              -> Mei's classic (fastest) Mandarin voice
    - Piper voices (chaowen / xiao_ya / kristin)-> shopkeeper voices + Mei's classic English voice

  Re-running is safe: finished files are skipped.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/setup-local-ai.ps1
  powershell -ExecutionPolicy Bypass -File Tools/setup-local-ai.ps1 -ModelSize 2B    # faster, weaker Mandarin
#>
param(
    [ValidateSet("2B", "4B", "9B")] [string]$ModelSize = "4B"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$SherpaVersion = "1.13.8"
$root = Join-Path (Split-Path -Parent $PSScriptRoot) "LocalAI"
$dl = Join-Path $root "downloads"
$models = Join-Path $root "models"
New-Item -ItemType Directory -Force -Path $root, $dl, $models | Out-Null
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
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    if ($zip.EndsWith(".zip")) { Expand-Archive -Force -Path $zip -DestinationPath $staging }
    else {
        # Windows' bsdtar handles .tar.bz2 natively (Git Bash's tar would not).
        & (Join-Path $env:SystemRoot "System32\tar.exe") -xjf $zip -C $staging
        if ($LASTEXITCODE -ne 0) { throw "tar failed on $zip" }
    }
    $hit = Get-ChildItem -Recurse -Path $staging -Filter $probe | Select-Object -First 1
    if (-not $hit) { throw "$probe not found in $zip" }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -Recurse -Force -Path (Join-Path $hit.DirectoryName "*") -Destination $dir
    Remove-Item -Recurse -Force $staging
}

# Model archive from the sherpa-onnx release pages, extracted to models/<name>.
function Get-SherpaModel([string]$tag, [string]$name, [string]$probe) {
    $archive = Join-Path $dl "$name.tar.bz2"
    Get-File "https://github.com/k2-fsa/sherpa-onnx/releases/download/$tag/$name.tar.bz2" $archive
    Expand-Into $archive (Join-Path $models $name) $probe
}

# --- LLM ---------------------------------------------------------------------
Write-Host "`n[1/4] llama.cpp server (Vulkan) + Qwen3.5-$ModelSize"
if (Test-Path (Join-Path $root "llama/llama-server.exe")) { Write-Host "  [skip] llama.cpp already installed" }
else {
    $llamaUrl = Get-LatestAsset "ggml-org/llama.cpp" "^llama-b\d+-bin-win-vulkan-x64\.zip$"
    $llamaZip = Join-Path $dl (Split-Path -Leaf $llamaUrl)
    Get-File $llamaUrl $llamaZip
    Expand-Into $llamaZip (Join-Path $root "llama") "llama-server.exe"
}
$ggufName = "Qwen3.5-$ModelSize-Q4_K_M.gguf"
Get-File "https://huggingface.co/unsloth/Qwen3.5-$ModelSize-GGUF/resolve/main/$ggufName" (Join-Path $models $ggufName)

# --- sherpa-onnx native runtime -------------------------------------------
Write-Host "`n[2/4] sherpa-onnx $SherpaVersion runtime"
$sherpaArchive = Join-Path $dl "sherpa-onnx-v$SherpaVersion-win-x64-shared-MD-Release.tar.bz2"
Get-File "https://github.com/k2-fsa/sherpa-onnx/releases/download/v$SherpaVersion/sherpa-onnx-v$SherpaVersion-win-x64-shared-MD-Release.tar.bz2" $sherpaArchive
Expand-Into $sherpaArchive (Join-Path $root "sherpa") "sherpa-onnx-c-api.dll"

# --- Speech recognition -------------------------------------------------------
Write-Host "`n[3/4] SenseVoice speech recognition"
Get-SherpaModel "asr-models" "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09" "model.int8.onnx"
# Qwen3-ASR (837 MB): re-hears lines that contain English (better at English / mixed sentences than SenseVoice).
Get-SherpaModel "asr-models" "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25" "decoder.int8.onnx"

# --- Voices -------------------------------------------------------------------
Write-Host "`n[4/4] Voices"
Get-SherpaModel "tts-models" "matcha-icefall-zh-en" "model-steps-3.onnx"
Get-File "https://github.com/k2-fsa/sherpa-onnx/releases/download/vocoder-models/vocos-16khz-univ.onnx" (Join-Path $models "vocos-16khz-univ.onnx")
Get-SherpaModel "tts-models" "vits-piper-zh_CN-chaowen-medium" "zh_CN-chaowen-medium.onnx"
Get-SherpaModel "tts-models" "vits-piper-zh_CN-xiao_ya-medium" "zh_CN-xiao_ya-medium.onnx"
Get-SherpaModel "tts-models" "vits-piper-en_US-kristin-medium" "en_US-kristin-medium.onnx"
# Mei's natural voice (347 MB). The fp32 model: the int8 one ran ~4x slower on a Ryzen 7 7435HS.
Get-SherpaModel "tts-models" "kokoro-multi-lang-v1_1" "voices.bin"

# Record the chosen LLM so the game can find it (other paths use the defaults in LocalAIConfig.cs).
@{ llmModel = "models/$ggufName" } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root "localai.json")

Write-Host "`nDone. Local AI stack is ready in $root"
