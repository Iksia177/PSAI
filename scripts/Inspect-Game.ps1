param([string] $GameDirectory)
. "$PSScriptRoot\Environment.ps1"
$game = Resolve-PsaiGameDirectory $GameDirectory
$managed = Join-Path $game 'Probably Stolen_Data\Managed'
$gameAssemblyPath = Join-Path $managed 'Assembly-CSharp.dll'
[void] [Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
$assembly = [DseCecil.AssemblyDefinition]::ReadAssembly($gameAssemblyPath)
try {
    $required = @(
        @('PlayerStore', 'GetStoreInventory', 0),
        @('PlayerStore', 'CanNextClient', 0),
        @('PlayerStore', 'TryCallNextClient', 0),
        @('PlayerStore', 'CanBuyItemFromTable', 0),
        @('PlayerStore', 'BuyItemFromTable', 0),
        @('PlayerStore', 'CanSellItemFromTable', 1),
        @('PlayerStore', 'SellItemFromTable', 0),
        @('DialogUIManager', 'GetCurrentDialog', 0),
        @('DialogUIManager', 'SelectChoice', 1),
        @('DialogUIManager', 'OnAdvancePressed', 0),
        @('NegociationUIManager', 'OnOfferAcceptClick', 0),
        @('NegociationUIManager', 'OnLeftArrowButtonClick', 0),
        @('NegociationUIManager', 'OnRightArrowButtonClick', 0),
        @('NegociationUIManager', 'OnConfirmPanelYes', 0),
        @('NegociationUIManager', 'IsTradeLocked', 0),
        @('ModHelper', 'MoveItemToFrontCounter', 1),
        @('ModHelper', 'GetModDataFolder', 1),
        @('ModHelper', 'ReadModDataFileText', 2),
        @('ModHelper', 'WriteModDataFileText', 3),
        @('ModHelper', 'ListModDataFiles', 1),
        @('ModScanner', 'Scan', 4),
        @('GameItemElement', 'GetTooltip', 0),
        @('GameItem', 'MayTarget', 1),
        @('GameItem', 'CanTarget', 1),
        @('GameItem', 'Target', 1),
        @('PhoneUIManager', 'PressNumber', 1),
        @('PhoneUIManager', 'StartCalling', 0),
        @('DialoguePhoneUIManager', 'OnAdvancePressed', 0),
        @('WildNetworkUI', 'get_IsOpen', 0),
        @('MapUIManager', 'Travel', 1),
        @('AfterhourLocation', 'GetDiscovered', 0),
        @('StoreSecButton', 'GetTooltip', 0),
        @('EmporiumEntry', 'ShowResponseWindow', 1),
        @('EmporiumEntry', 'GetAllOwnedItemsByTag', 1),
        @('PixelWindow', 'IsVisible', 0),
        @('PaperUIManager', 'CloseBrewerCard', 0),
        @('PlayerStore', 'GetRentValue', 0),
        @('StoreClient', 'SetWanted', 3),
        @('MachinerySlider', 'SetValue', 2),
        @('MachineryRangeSlider', 'SetValues', 3),
        @('WaterHelper', 'TransferLiquid', 2),
        @('Shotgun', 'CanPickup', 0),
        @('Shotgun', 'OnClick', 0),
        @('StoreUIManager', 'CanShoot', 0),
        @('StoreUIManager', 'Shoot', 0),
        @('StoreUIManager', 'ExitShootingMode', 0),
        @('GeneralHelper', 'IsItemEffectivelyOwned', 1),
        @('StoreDoor', 'OnClick', 0),
        @('StoreDoor', 'GetTooltip', 0)
    )
    $checks = @($required | ForEach-Object {
        $typeName, $methodName, $parameterCount = $_
        $type = $assembly.MainModule.Types | Where-Object Name -EQ $typeName
        $matches = @()
        if ($type) {
            $matches = @($type.Methods | Where-Object {
                $_.Name -eq $methodName -and $_.Parameters.Count -eq $parameterCount
            })
        }
        [pscustomobject] @{
            type = $typeName
            method = $methodName
            parameterCount = $parameterCount
            found = $matches.Count -gt 0
            signatures = @($matches | ForEach-Object FullName)
        }
    })
    $store = $assembly.MainModule.Types | Where-Object Name -EQ 'PlayerStore'
    $version = ($store.Fields | Where-Object Name -EQ 'VERSION_NUMBER').Constant
    $steamManifest = Join-Path (Split-Path (Split-Path $game -Parent) -Parent) 'appmanifest_4348910.acf'
    $buildId = $null
    if (Test-Path -LiteralPath $steamManifest) {
        $text = Get-Content -LiteralPath $steamManifest -Raw
        $buildId = [regex]::Match($text, '"buildid"\s+"(\d+)"').Groups[1].Value
    }
    $report = [ordered] @{
        checkedAt = [DateTimeOffset]::Now.ToString('o')
        gameVersion = $version
        steamBuildId = $buildId
        assemblySha256 = (Get-FileHash -LiteralPath $gameAssemblyPath -Algorithm SHA256).Hash
        metadataChecksPassed = @($checks | Where-Object { -not $_.found }).Count -eq 0
        checks = $checks
    }
    $output = Join-Path $PsaiRoot 'artifacts'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'compatibility.json') -Encoding utf8
    Write-Output "$version / Steam build $buildId / $($checks.Count) metadata checks"
    if (-not $report.metadataChecksPassed) { throw 'Required method metadata is missing. See artifacts/compatibility.json.' }
}
finally {
    $assembly.Dispose()
}
