param([string] $GameDirectory)
. "$PSScriptRoot\Environment.ps1"
$game = Resolve-PsaiGameDirectory $GameDirectory
$managed = Join-Path $game 'Probably Stolen_Data\Managed'
$package = Join-Path $PsaiRoot 'artifacts\psai'
$manifestPath = Join-Path $package 'manifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Run scripts/Build.ps1 first.' }
[xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
$dlls = [string[]] @($manifest.Manifest.Assemblies.Assembly | ForEach-Object {
    $path = [IO.Path]::GetFullPath((Join-Path $package ([string] $_)))
    if (-not $path.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Manifest assembly path escapes the package.'
    }
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing assembly $path" }
    $path
})
$allowed = [string[]] @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object BaseName)

# Execute the installed game's scanner. The mod being inspected is never loaded.
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = [Reflection.AssemblyName]::new($eventArgs.Name).Name
    $path = Join-Path $managed ($name + '.dll')
    if (Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
    $scanner = $assembly.GetType('ModScanner', $true)
    $method = $scanner.GetMethod('Scan', [Reflection.BindingFlags] 'Public,NonPublic,Static')
    $arguments = [object[]] @([string[]] $dlls, [string[]] $allowed, [string] $package, [bool] $true)
    $result = $method.Invoke($null, $arguments)
    $report = [ordered] @{
        scannerVersion = $scanner.GetField('VERSION').GetRawConstantValue()
        methodsScanned = $result.MethodsScanned
        refused = $result.Refused
        error = $result.Error
        capabilities = @($result.Capabilities)
        hardFlags = @($result.HardFlags | ForEach-Object { $_ | Out-String })
        findings = $result.FormatFindings(100)
    }
    $json = $report | ConvertTo-Json -Depth 5
    $json | Set-Content -LiteralPath (Join-Path $PsaiRoot 'artifacts\scan.json') -Encoding utf8
    Write-Output $json
    if ($result.Refused -or $result.Error -or $result.MethodsScanned -eq 0 -or
        $result.Capabilities.Count -gt 0 -or $result.HardFlags.Count -gt 0) {
        throw 'Preparation skeleton must scan successfully with zero capabilities and zero hard flags.'
    }
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
