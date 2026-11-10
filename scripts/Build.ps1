param([string] $GameDirectory)
. "$PSScriptRoot\Environment.ps1"
$game = Resolve-PsaiGameDirectory $GameDirectory
$dotnet = Resolve-PsaiDotnet
$project = Join-Path $PsaiRoot 'src\PSAI.Mod\PSAI.Mod.csproj'
$feed = Join-Path $PsaiRoot '.cache\empty-feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $PsaiRoot '.cache\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'

Push-Location $PsaiRoot
try {
    & $dotnet restore $project --source $feed "-p:GameDirectory=$game"
    if ($LASTEXITCODE -ne 0) { throw 'Mod restore failed.' }
    & $dotnet build $project --configuration Release --no-restore "-p:GameDirectory=$game"
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }

    $package = Join-Path $PsaiRoot 'artifacts\psai'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PsaiRoot 'src\PSAI.Mod\bin\Release\netstandard2.1\PSAI.Mod.dll') -Destination $package
    Copy-Item -LiteralPath (Join-Path $PsaiRoot 'src\PSAI.Mod\manifest.xml') -Destination $package
    Write-Output "Package prepared at $package"
}
finally {
    Pop-Location
}
