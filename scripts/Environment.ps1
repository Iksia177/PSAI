Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PsaiRoot = Split-Path -Parent $PSScriptRoot

function Resolve-PsaiGameDirectory([string] $GameDirectory) {
    if (-not $GameDirectory) {
        $configPath = Join-Path $PsaiRoot 'psai.local.json'
        if (-not (Test-Path -LiteralPath $configPath)) {
            throw 'Pass -GameDirectory or create psai.local.json from psai.local.example.json.'
        }
        $GameDirectory = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).gameDirectory
    }
    $resolved = (Resolve-Path -LiteralPath $GameDirectory).Path
    $assemblyPath = Join-Path $resolved 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "Game assembly not found at $assemblyPath"
    }
    return $resolved
}

function Resolve-PsaiDotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $installed = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $installed) { return $installed }
    throw 'Install the SDK specified by global.json, or add dotnet to PATH.'
}
