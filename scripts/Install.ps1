param([string] $GameDirectory)
. "$PSScriptRoot\Environment.ps1"
$game = Resolve-PsaiGameDirectory $GameDirectory
if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) {
    throw 'Exit Probably Stolen before installing the bridge.'
}
& "$PSScriptRoot\Scan.ps1" -GameDirectory $game
$package = Join-Path $PsaiRoot 'artifacts\psai'
$destination = Join-Path $game 'Mods\psai'
$receiptPath = Join-Path $PsaiRoot '.cache\installed.json'
$previous = $null
if (Test-Path -LiteralPath $receiptPath) { $previous = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json }
if (Test-Path -LiteralPath $destination) {
    foreach ($file in (Get-ChildItem -LiteralPath $destination -Force)) {
        $source = Join-Path $package $file.Name
        if ($file.PSIsContainer -or -not (Test-Path -LiteralPath $source)) {
            throw 'Existing Mods/psai differs from this package. Preserve or move it before installation.'
        }
        $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
        $samePackage = $hash -eq (Get-FileHash -LiteralPath $source).Hash
        $knownInstall = $previous -and $previous.destination -eq $destination -and $previous.hashes.($file.Name) -eq $hash
        if (-not $samePackage -and -not $knownInstall) { throw "Unknown installed file $($file.FullName). Preserve it before installation." }
    }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$hashes = @{}
foreach ($name in @('PSAI.Mod.dll', 'manifest.xml')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $destination $name)
    if ((Get-FileHash -LiteralPath (Join-Path $package $name)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash) { throw "Install verification failed for $name" }
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash
}
New-Item -ItemType Directory -Path (Split-Path -Parent $receiptPath) -Force | Out-Null
@{ destination = $destination; hashes = $hashes } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
Write-Output "Verified installation at $destination"
