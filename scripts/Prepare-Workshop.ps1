param()
. "$PSScriptRoot\Environment.ps1"
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\Scan.ps1"
$package = Join-Path $PsaiRoot 'artifacts\psai'
[xml] $manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.xml') -Raw
$version = [string] $manifest.Manifest.ModVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
$content = Join-Path $PsaiRoot "artifacts\workshop\$version\content"
$local = Join-Path $PsaiRoot '.local-dev\workshop'
New-Item -ItemType Directory -Path $content, $local -Force | Out-Null
foreach ($file in (Get-ChildItem -LiteralPath $content -Force)) {
    if ($file.PSIsContainer -or $file.Name -notin @('PSAI.Mod.dll', 'manifest.xml')) { throw 'Unexpected upload content. Review it before publishing.' }
}
foreach ($name in @('PSAI.Mod.dll', 'manifest.xml')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $content $name)
    if ((Get-FileHash -LiteralPath (Join-Path $package $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $content $name)).Hash) { throw 'Upload package hash mismatch.' }
}

# A simple typographic cover, generated without redistributing game artwork.
Add-Type -AssemblyName System.Drawing
$preview = Join-Path $PsaiRoot "artifacts\workshop\$version\preview.png"
$bitmap = [Drawing.Bitmap]::new(1024, 1024)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(97, 238, 198))
$white = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(238, 244, 250))
$muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(165, 182, 201))
$title = [Drawing.Font]::new('Segoe UI', 154, [Drawing.FontStyle]::Bold)
$subtitle = [Drawing.Font]::new('Segoe UI', 29, [Drawing.FontStyle]::Regular)
$small = [Drawing.Font]::new('Segoe UI', 24, [Drawing.FontStyle]::Regular)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(17, 26, 39))
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.FillRectangle($brush, 80, 100, 112, 12)
    $graphics.DrawString('PSAI', $title, $white, 62, 270)
    $graphics.DrawString('PROBABLY STOLEN', $subtitle, $brush, 84, 545)
    $graphics.DrawString('AGENT INTERFACE', $subtitle, $white, 84, 610)
    $graphics.FillRectangle($brush, 84, 733, 856, 3)
    $graphics.DrawString("v$version", $small, $muted, 84, 792)
    $graphics.DrawString('Iksia', $small, $muted, 820, 792)
    $bitmap.Save($preview, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($resource in @($title, $subtitle, $small, $brush, $white, $muted, $graphics, $bitmap)) { $resource.Dispose() }
}
if ((Get-Item -LiteralPath $preview).Length -ge 1MB) { throw 'Workshop preview exceeds 1 MB.' }

$vdf = Join-Path $local 'psai.vdf'
$itemId = '0'
if (Test-Path -LiteralPath $vdf) {
    $previous = Get-Content -LiteralPath $vdf -Raw
    if ($previous -notmatch '"publishedfileid"\s+"(\d+)"') { throw 'Existing upload config has no valid item ID. Preserve and review it.' }
    $itemId = $Matches[1]
}
$publishedConfig = Join-Path $PsaiRoot 'workshop\item.json'
if (Test-Path -LiteralPath $publishedConfig) {
    $published = Get-Content -LiteralPath $publishedConfig -Raw | ConvertFrom-Json
    if ($published.appid -ne '4348910' -or $published.publishedfileid -notmatch '^\d+$') { throw 'Invalid published Workshop item configuration.' }
    if ($itemId -ne '0' -and $itemId -ne $published.publishedfileid) { throw 'Local upload ID differs from the published item. Review before updating.' }
    $itemId = [string] $published.publishedfileid
}
function Escape-PsaiVdf([string] $value) { return $value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '') }
$description = (Get-Content -LiteralPath (Join-Path $PsaiRoot 'workshop\description.txt') -Raw).Trim()
if ([Text.Encoding]::UTF8.GetByteCount($description) -gt 8000) { throw 'Workshop description exceeds 8000 UTF-8 bytes.' }
$text = @"
"workshopitem"
{
    "appid" "4348910"
    "publishedfileid" "$itemId"
    "contentfolder" "$(Escape-PsaiVdf $content)"
    "previewfile" "$(Escape-PsaiVdf $preview)"
    "visibility" "0"
    "title" "PSAI - Probably Stolen Agent Interface"
    "description" "$(Escape-PsaiVdf $description)"
    "changenote" "PSAI $version - event dialogue priority, prompt attention and early-game UI support."
}
"@
[IO.File]::WriteAllText($vdf, $text, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared public Workshop upload: $vdf"
Write-Output "Item ID: $itemId; version: $version; files: PSAI.Mod.dll, manifest.xml"
