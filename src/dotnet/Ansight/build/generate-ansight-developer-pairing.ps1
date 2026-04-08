param(
    [Parameter(Mandatory = $true)]
    [string]$SourceFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

if (-not (Test-Path -LiteralPath $SourceFile)) {
    exit 0
}

function Add-UniqueHostAddress {
    param(
        [System.Collections.Generic.List[string]]$HostAddresses,
        [string]$Address
    )

    if ([string]::IsNullOrWhiteSpace($Address)) {
        return
    }

    $normalizedAddress = $Address.Trim()
    foreach ($existingAddress in $HostAddresses) {
        if ($existingAddress.Equals($normalizedAddress, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    $HostAddresses.Add($normalizedAddress)
}

function Add-InterfaceHostAddresses {
    param(
        [System.Collections.Generic.List[string]]$HostAddresses,
        [int]$InterfaceIndex
    )

    Get-NetIPAddress -InterfaceIndex $InterfaceIndex -ErrorAction SilentlyContinue |
        Where-Object {
            $_.IPAddress -and (
                ($_.AddressFamily -eq 'IPv4' -and $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*') -or
                ($_.AddressFamily -eq 'IPv6' -and $_.IPAddress -ne '::1' -and $_.IPAddress -notmatch '^fe80:')
            )
        } |
        Sort-Object @{ Expression = { if ($_.AddressFamily -eq 'IPv4') { 0 } else { 1 } } } |
        ForEach-Object {
            Add-UniqueHostAddress -HostAddresses $HostAddresses -Address $_.IPAddress
        }
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
$hostAddresses = [System.Collections.Generic.List[string]]::new()
$hostName = $env:COMPUTERNAME
$defaultRoute = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Sort-Object RouteMetric, InterfaceMetric |
    Select-Object -First 1

if ($wifiAdapter) {
    $ssidMatch = netsh wlan show interfaces 2>$null |
        Select-String '^\s*SSID\s*:\s*(.+)$' |
        Select-Object -First 1

    if ($ssidMatch) {
        $wifiName = $ssidMatch.Matches[0].Groups[1].Value.Trim()
    }
}

if ($defaultRoute) {
    Add-InterfaceHostAddresses -HostAddresses $hostAddresses -InterfaceIndex $defaultRoute.IfIndex
}

if ($wifiAdapter -and (-not $defaultRoute -or $wifiAdapter.IfIndex -ne $defaultRoute.IfIndex)) {
    Add-InterfaceHostAddresses -HostAddresses $hostAddresses -InterfaceIndex $wifiAdapter.IfIndex
}

$hostAddress = $hostAddresses |
    Where-Object { $_ -notmatch ':' } |
    Select-Object -First 1

if (-not $hostAddress) {
    $hostAddress = $hostAddresses | Select-Object -First 1
}

$pairingConfig = Get-Content -LiteralPath $SourceFile -Raw | ConvertFrom-Json
$document = [ordered]@{
    schema = 'ansight.pairing-ticket.v1'
    config = $pairingConfig
    discovery = [ordered]@{
        schema = 'ansight.discovery-hint.v1'
        source = 'developer-pairing-msbuild'
        hostAddresses = @($hostAddresses)
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
Write-Host "Ansight developer pairing discovery: source=$SourceFile output=$OutputFile wifi=$(if ($wifiName) { $wifiName } else { '<unknown>' }) hostName=$(if ($hostName) { $hostName } else { '<unknown>' }) hostAddress=$(if ($hostAddress) { $hostAddress } else { '<unknown>' }) hostAddresses=$(if ($hostAddresses.Count -gt 0) { $hostAddresses -join ', ' } else { '<unknown>' })"
