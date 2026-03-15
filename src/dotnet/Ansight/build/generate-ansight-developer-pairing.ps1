param(
    [Parameter(Mandatory = $true)]
    [string]$SourceFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

if (-not (Test-Path -LiteralPath $SourceFile)) {
    exit 0
}

$wifiAdapter = Get-NetAdapter -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Status -eq 'Up' -and (
            $_.Name -match 'Wi-?Fi|Wireless' -or
            $_.InterfaceDescription -match 'Wi-?Fi|Wireless|802\.11'
        )
    } |
    Select-Object -First 1

$wifiName = ''
$hostAddress = ''
$hostName = $env:COMPUTERNAME

if ($wifiAdapter) {
    $ssidMatch = netsh wlan show interfaces 2>$null |
        Select-String '^\s*SSID\s*:\s*(.+)$' |
        Select-Object -First 1

    if ($ssidMatch) {
        $wifiName = $ssidMatch.Matches[0].Groups[1].Value.Trim()
    }

    $hostAddress = Get-NetIPAddress -InterfaceIndex $wifiAdapter.IfIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -First 1 -ExpandProperty IPAddress
}

$pairingConfig = Get-Content -LiteralPath $SourceFile -Raw | ConvertFrom-Json
$document = [ordered]@{
    schema = 'ansight.pairing-bootstrap.v1'
    pairingConfig = $pairingConfig
    discovery = [ordered]@{
        schema = 'ansight.discovery-hint.v1'
        source = 'developer-pairing-msbuild'
        hostAddress = $hostAddress
        hostName = $hostName
        wifiName = $wifiName
        capturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
}

$directory = Split-Path -Parent $OutputFile
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$document | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
