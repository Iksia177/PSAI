param(
    [Parameter(Mandatory)] [string[]] $TypeName,
    [string] $Method = '.',
    [switch] $IL,
    [switch] $Fields,
    [string] $GameDirectory
)
. "$PSScriptRoot\Environment.ps1"
$game = Resolve-PsaiGameDirectory $GameDirectory
$managed = Join-Path $game 'Probably Stolen_Data\Managed'
[void] [Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
$assembly = [DseCecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
try {
    foreach ($name in $TypeName) {
        $type = $assembly.MainModule.Types | Where-Object Name -EQ $name
        if (-not $type) { Write-Output "Missing type $name"; continue }
        Write-Output "TYPE $name BASE $($type.BaseType)"
        if ($Fields) {
            $type.Fields | ForEach-Object { "$($_.Attributes) $($_.FullName)" }
        }
        foreach ($member in ($type.Methods | Where-Object Name -Match $Method)) {
            Write-Output "$($member.Attributes) $($member.FullName)"
            if ($IL -and $member.HasBody) {
                $member.Body.Instructions | ForEach-Object ToString
            }
        }
    }
}
finally { $assembly.Dispose() }
