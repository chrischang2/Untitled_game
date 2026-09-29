<#
.SYNOPSIS
  Downloads the free third-party art, audio and fonts into Assets/ThirdParty (gitignored).
  Kenney kits (CC0), OpenGameArt audio (CC0) and Google Fonts (OFL) - see CREDITS.md.

  Re-running is safe; use -Force to wipe and re-download.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/fetch-assets.ps1
#>
param([switch]$Force)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$project = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $project "Assets/ThirdParty"
$cache = Join-Path $project "Library/AssetDownloads"   # Library/ is gitignored and disposable
New-Item -ItemType Directory -Force -Path $cache | Out-Null

if ($Force -and (Test-Path $dest)) { Remove-Item -Recurse -Force $dest }
if ((Test-Path (Join-Path $dest "Kenney")) -and -not $Force) {
    Write-Host "Assets/ThirdParty already present (use -Force to re-download)."
    exit 0
}

$ua = @{ "User-Agent" = "Mozilla/5.0 (WillowLake asset fetcher)" }

function Get-File([string]$url, [string]$out) {
    if (Test-Path $out) { return $out }
    Write-Host "  [get ] $url"
    & curl.exe -L --fail --retry 4 -A "Mozilla/5.0" -o "$out.part" $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    Move-Item -Force "$out.part" $out
    return $out
}

function Get-KenneyZip([string]$slug) {
    # Kenney's download links contain a content hash, so read the current one from the asset page.
    $page = (Invoke-WebRequest -UseBasicParsing -Headers $ua -Uri "https://kenney.nl/assets/$slug").Content
    $m = [regex]::Match($page, 'https://kenney\.nl/media/pages/assets/[^"]+\.zip')
    if (-not $m.Success) { throw "Could not find the download link on https://kenney.nl/assets/$slug" }
    $zip = Get-File $m.Value (Join-Path $cache "$slug.zip")
    $dir = Join-Path $cache $slug
    if (-not (Test-Path $dir)) { Expand-Archive -Force -Path $zip -DestinationPath $dir }
    return $dir
}

function Copy-Kit([string]$slug, [string]$kitName, [string[]]$patterns, [string[]]$exclude = @()) {
    Write-Host "Kenney $kitName"
    $src = Get-KenneyZip $slug
    $fbxDir = Join-Path $src "Models/FBX format"
    $out = Join-Path $dest "Kenney/$kitName"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Copy-Item (Join-Path $src "License.txt") (Join-Path $out "License.txt")
    foreach ($p in $patterns) {
        Get-ChildItem -Path $fbxDir -Filter "$p.fbx" | Where-Object {
            $n = $_.BaseName; -not ($exclude | Where-Object { $n -like $_ })
        } | Copy-Item -Destination $out
    }
    $tex = Join-Path $fbxDir "Textures"
    if (Test-Path $tex) { Copy-Item -Recurse -Force $tex (Join-Path $out "Textures") }
}

# ---------------------------------------------------------------- Kenney models
Copy-Kit "nature-kit" "NatureKit" @("tree_*", "rock_*", "stone_*", "lily_*", "plant_*", "flower_*", "grass*", "mushroom_*", "log*", "stump_*",
    "campfire_*", "canoe*", "tent_*", "fence_simple*", "fence_planks*", "path_stone*", "path_wood*", "hanging_moss", "sign", "bridge_wood*", "pot_*", "crop_pumpkin") @("tree_palm*")
Copy-Kit "survival-kit" "SurvivalKit" @("fish*", "bucket", "campfire-*", "barrel*", "box*", "bedroll*", "signpost*", "chest", "tent*", "workbench",
    "resource-wood", "tree-log*", "rock-*", "patch-grass*", "grass*", "floor-old", "fence", "structure-roof", "structure", "structure-floor")
Copy-Kit "pirate-kit" "PirateKit" @("boat-row-*", "structure-platform-dock*", "structure-fence*", "platform-planks", "barrel", "crate", "crate-bottles",
    "tool-paddle", "bottle*", "flag", "flag-pennant", "grass-plant", "patch-grass*", "rocks-a", "rocks-b", "rocks-c")
Copy-Kit "holiday-kit" "HolidayKit" @("cabin-*", "lantern*", "bench*", "floor-wood", "rocks-*")
Copy-Kit "mini-characters" "MiniCharacters" @("character-*")
Copy-Kit "cube-pets" "CubePets" @("animal-cat", "animal-dog", "animal-fish", "animal-chick", "animal-bunny", "animal-crab", "animal-fox", "animal-deer", "animal-parrot", "animal-beaver")
Copy-Kit "fantasy-town-kit" "FantasyTown" @("lantern", "stall-bench", "stall-stool", "cart", "fence*", "hedge", "hedge-curved", "rock-*", "tree*", "poles*", "banner-*", "watermill*", "wheel")
Copy-Kit "food-kit" "FoodKit" @("fish", "fish-bones", "cup-tea", "mug", "pot-stew", "pot-stew-lid", "bowl-soup", "loaf", "apple", "skewer", "mussel*")

# ---------------------------------------------------------------- Audio
$audio = Join-Path $dest "Audio/Resources"
foreach ($d in "Music", "Ambience", "SFX") { New-Item -ItemType Directory -Force -Path (Join-Path $audio $d) | Out-Null }

Write-Host "Kenney audio"
$ui = Get-KenneyZip "interface-sounds"
foreach ($n in "click_002", "confirmation_002", "open_002", "close_002", "pluck_001", "pluck_002", "drop_002", "select_001", "toggle_001", "maximize_006", "minimize_006", "error_004") {
    Copy-Item (Join-Path $ui "Audio/$n.ogg") (Join-Path $audio "SFX/ui_$n.ogg")
}
Copy-Item (Join-Path $ui "License.txt") (Join-Path $audio "SFX/License_KenneyInterfaceSounds.txt")
$impact = Get-KenneyZip "impact-sounds"
foreach ($i in 0..4) {
    foreach ($kind in "grass", "wood") { Copy-Item (Join-Path $impact "Audio/footstep_${kind}_00$i.ogg") (Join-Path $audio "SFX/") }
}
Copy-Item (Join-Path $impact "License.txt") (Join-Path $audio "SFX/License_KenneyImpactSounds.txt")
$rpg = Get-KenneyZip "rpg-audio"
foreach ($n in "cloth1", "cloth2", "creak1", "creak2", "bookOpen", "bookClose", "bookFlip1", "dropLeather", "handleCoins") {
    Copy-Item (Join-Path $rpg "Audio/$n.ogg") (Join-Path $audio "SFX/rpg_$n.ogg")
}
Copy-Item (Join-Path $rpg "License.txt") (Join-Path $audio "SFX/License_KenneyRPGAudio.txt")

Write-Host "OpenGameArt audio (CC0)"
$oga = "https://opengameart.org/sites/default/files"
$files = [ordered]@{
    "Music/a_small_fire_will_do.wav"  = "a_small_fire_will_do.wav"
    "Music/apple_cider.ogg"           = "apple_cider.ogg"
    "Music/catmint.ogg"               = "catmint_in_c_major_looped.ogg"
    "Music/daisy.ogg"                 = "daisy_0.ogg"
    "Music/cozy_puzzle_in-game_1.ogg" = "cozy_puzzle_in-game_1_bpm118_0.ogg"
    "Ambience/birds.ogg"              = "birds-isaiah658_0.ogg"
    "Ambience/crickets.mp3"           = "crickets-oneloop.mp3"
    "Ambience/forest.mp3"             = "Forest_Ambience.mp3"
    "SFX/reel.wav"                    = "reel.wav"
    "SFX/cast_splash.wav"             = "splash.wav"
    "SFX/bloop.wav"                   = "bloop.wav"
    "SFX/catch_normal.wav"            = "point_normal.wav"
    "SFX/catch_special.wav"           = "point_special.wav"
    "SFX/escape.wav"                  = "uhoh.wav"
    "SFX/splash_big1.wav"             = "splash1_0.wav"
    "SFX/splash_big2.wav"             = "splash2_0.wav"
    "SFX/plop.ogg"                    = "waterReentry.ogg"
    "SFX/water_small.ogg"             = "water.ogg"
}
foreach ($k in $files.Keys) {
    $cached = Get-File "$oga/$($files[$k])" (Join-Path $cache ("oga_" + $files[$k]))
    Copy-Item $cached (Join-Path $audio $k)
}
$slimeZip = Get-File "$oga/water-splash-slime-sfx.zip" (Join-Path $cache "water-splash-slime-sfx.zip")
$slime = Join-Path $cache "water-splash-slime-sfx"
if (-not (Test-Path $slime)) { Expand-Archive -Force -Path $slimeZip -DestinationPath $slime }
function Find-In([string]$root, [string]$name) { (Get-ChildItem -Recurse -Path $root -Filter $name | Select-Object -First 1).FullName }
foreach ($n in "loop_water_01", "loop_water_02", "loop_water_03", "loop_rain") { Copy-Item (Find-In $slime "$n.ogg") (Join-Path $audio "Ambience/$n.ogg") }
foreach ($n in "splash_01", "splash_02", "splash_03", "splash_04", "splash_05", "splash_06", "bubble_01", "bubble_02", "bubble_03") {
    Copy-Item (Find-In $slime "$n.ogg") (Join-Path $audio "SFX/$n.ogg")
}

# ---------------------------------------------------------------- Fonts (OFL)
Write-Host "Fonts"
$fonts = Join-Path $dest "Fonts"
New-Item -ItemType Directory -Force -Path (Join-Path $fonts "Resources") | Out-Null
$gf = "https://github.com/google/fonts/raw/main/ofl"
Copy-Item (Get-File "$gf/varelaround/VarelaRound-Regular.ttf" (Join-Path $cache "VarelaRound-Regular.ttf")) (Join-Path $fonts "Resources/")
Copy-Item (Get-File "$gf/varelaround/OFL.txt" (Join-Path $cache "VarelaRound-OFL.txt")) (Join-Path $fonts "VarelaRound-OFL.txt")
Copy-Item (Get-File "$gf/lilitaone/LilitaOne-Regular.ttf" (Join-Path $cache "LilitaOne-Regular.ttf")) (Join-Path $fonts "Resources/")
Copy-Item (Get-File "$gf/lilitaone/OFL.txt" (Join-Path $cache "LilitaOne-OFL.txt")) (Join-Path $fonts "LilitaOne-OFL.txt")

$count = (Get-ChildItem -Recurse -File -Path $dest | Where-Object { $_.Extension -ne ".meta" }).Count
Write-Host "`nDone: $count files in Assets/ThirdParty"
