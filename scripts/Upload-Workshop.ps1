param([string] $SteamAccount)
. "$PSScriptRoot\Environment.ps1"
$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'PSAI Workshop Upload - Steam login'
$steamcmd = Join-Path $PsaiRoot '.cache\steamcmd\steamcmd.exe'
if (-not (Test-Path -LiteralPath $steamcmd)) { throw 'Install Valve SteamCMD in .cache/steamcmd first.' }
& "$PSScriptRoot\Prepare-Workshop.ps1"
if ([string]::IsNullOrWhiteSpace($SteamAccount)) { $SteamAccount = Read-Host 'Steam login account (not profile name)' }
if ([string]::IsNullOrWhiteSpace($SteamAccount)) { throw 'A Steam account owning the game is required.' }
$vdf = Join-Path $PsaiRoot '.local-dev\workshop\psai.vdf'
Push-Location (Split-Path -Parent $steamcmd)
try {
    # SteamCMD handles passwords/Steam Guard interactively; never store them in this script or VDF.
    & $steamcmd '+login' $SteamAccount '+workshop_build_item' $vdf '+quit'
    if ($LASTEXITCODE -ne 0) { throw "SteamCMD exited with code $LASTEXITCODE. Keep the VDF and inspect the upload log before retrying." }
} finally { Pop-Location }
Write-Host 'Upload command completed. Keep .local-dev/workshop/psai.vdf for updates.'
Read-Host 'Press Enter to close this window'
