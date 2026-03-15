#!/bin/bash

set -u

source_file="${1:-}"
output_path="${2:-}"

if [ -z "$source_file" ] || [ -z "$output_path" ] || [ ! -f "$source_file" ]; then
  exit 0
fi

wifi_device="$(networksetup -listallhardwareports 2>/dev/null | awk '/Wi-Fi|AirPort/{getline; print $2; exit}')"
wifi_name=""
host_address=""
host_name="$(hostname 2>/dev/null || true)"

if [ -n "$wifi_device" ]; then
  wifi_name="$(networksetup -getairportnetwork "$wifi_device" 2>/dev/null | sed 's/^Current Wi-Fi Network: //')"
  host_address="$(ipconfig getifaddr "$wifi_device" 2>/dev/null || true)"
fi

if [ "$wifi_name" = "You are not associated with an AirPort network." ]; then
  wifi_name=""
fi

json_escape() {
  local value="${1:-}"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\r'/}"
  value="${value//$'\n'/}"
  printf '%s' "$value"
}

mkdir -p "$(dirname "$output_path")"
pairing_config_json="$(cat "$source_file")"

cat > "$output_path" <<EOF
{
  "schema": "ansight.pairing-bootstrap.v1",
  "pairingConfig": $pairing_config_json,
  "discovery": {
    "schema": "ansight.discovery-hint.v1",
    "source": "developer-pairing-msbuild",
    "hostAddress": "$(json_escape "$host_address")",
    "hostName": "$(json_escape "$host_name")",
    "wifiName": "$(json_escape "$wifi_name")",
    "capturedAt": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  }
}
EOF
