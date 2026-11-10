. "$PSScriptRoot\Environment.ps1"
$dotnet = Resolve-PsaiDotnet
$env:DOTNET_CLI_HOME = Join-Path $PsaiRoot '.cache\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PsaiRoot 'tests\PSAI.Checks\PSAI.Checks.csproj'
$feed = Join-Path $PsaiRoot '.cache\empty-feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
& $dotnet restore $project --source $feed
if ($LASTEXITCODE -ne 0) { throw 'Payment test restore failed.' }
& $dotnet run --project $project --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Payment regression checks failed.' }
