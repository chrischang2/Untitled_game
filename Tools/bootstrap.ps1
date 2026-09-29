<#
.SYNOPSIS
  One command to get a fresh clone playable:
    1. download the free art/audio/fonts          (Tools/fetch-assets.ps1   -> Assets/ThirdParty)
    2. download the local AI stack                (Tools/setup-local-ai.ps1 -> LocalAI)
    3. run Unity headless to configure the project and generate the scene
  Then open the project in Unity and press Play on Assets/Scenes/WillowLake.unity.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/bootstrap.ps1
  powershell -ExecutionPolicy Bypass -File Tools/bootstrap.ps1 -SkipAI
#>
param(
    [switch]$SkipAI,
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"
$project = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "fetch-assets.ps1")
if (-not $SkipAI) { & (Join-Path $PSScriptRoot "setup-local-ai.ps1") }

if (-not $UnityPath) {
    $version = (Select-String -Path (Join-Path $project "ProjectSettings/ProjectVersion.txt") -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value.Trim()
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}
if (-not (Test-Path $UnityPath)) { throw "Unity not found at $UnityPath (install it via Unity Hub or pass -UnityPath)" }

$log = Join-Path $project "Captures/unity-bootstrap.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
Write-Host "`nRunning Unity headless (first import takes a few minutes)... log: $log"
$p = Start-Process -FilePath $UnityPath -Wait -PassThru -ArgumentList @(
    "-batchmode", "-quit", "-projectPath", "`"$project`"",
    "-executeMethod", "UntitledGame.EditorTools.BuildTools.Bootstrap", "-logFile", "`"$log`"")
if ($p.ExitCode -ne 0) { throw "Unity bootstrap failed (exit $($p.ExitCode)); see $log" }
Write-Host "Done! Open the project in Unity and play Assets/Scenes/WillowLake.unity."
