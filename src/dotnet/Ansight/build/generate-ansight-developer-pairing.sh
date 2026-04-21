#!/bin/bash

set -u

source_file="${1:-}"
output_path="${2:-}"

if [ -z "$output_path" ]; then
  exit 0
fi

if [ -z "$source_file" ] || [ ! -f "$source_file" ]; then
  echo "Ansight developer pairing requires a signed pairing JSON source file. Missing: $source_file" >&2
  exit 1
fi

wifi_device="$(networksetup -listallhardwareports 2>/dev/null | awk '/Wi-Fi|AirPort/{getline; print $2; exit}')"
wifi_name=""
host_address=""
host_addresses=()
host_name="$(hostname 2>/dev/null || true)"
default_device="$(route -n get default 2>/dev/null | awk '/interface:/{print $2; exit}')"

append_unique_host_address() {
  local candidate="${1:-}"
  if [ -z "$candidate" ]; then
    return
  fi

  local existing
  if [ "${#host_addresses[@]}" -gt 0 ]; then
    for existing in "${host_addresses[@]}"; do
      if [ "$existing" = "$candidate" ]; then
        return
      fi
    done
  fi

  host_addresses+=("$candidate")
}

collect_interface_addresses() {
  local device="${1:-}"
  if [ -z "$device" ]; then
    return
  fi

  while IFS= read -r address; do
    append_unique_host_address "$address"
  done < <(
    ifconfig "$device" 2>/dev/null | awk '
      /^[[:space:]]*inet / {
        address = $2
        if (address != "127.0.0.1" && address !~ /^169\.254\./) {
          print address
        }
      }
      /^[[:space:]]*inet6 / {
        address = $2
        sub(/%.*/, "", address)
        lower = tolower(address)
        if (lower != "::1" && lower !~ /^fe80:/) {
          print address
        }
      }
    '
  )
}

if [ -n "$wifi_device" ]; then
  wifi_name="$(networksetup -getairportnetwork "$wifi_device" 2>/dev/null | sed 's/^Current Wi-Fi Network: //')"
fi

collect_interface_addresses "$default_device"
if [ -n "$wifi_device" ] && [ "$wifi_device" != "$default_device" ]; then
  collect_interface_addresses "$wifi_device"
fi

if [ "${#host_addresses[@]}" -gt 0 ]; then
  for address in "${host_addresses[@]}"; do
    case "$address" in
      *:*)
        ;;
      *)
        host_address="$address"
        break
        ;;
    esac
  done
fi

if [ -z "$host_address" ] && [ "${#host_addresses[@]}" -gt 0 ]; then
  host_address="${host_addresses[0]}"
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

json_array() {
  local values=("$@")
  local json="["
  local index
  for ((index = 0; index < ${#values[@]}; index++)); do
    if [ "$index" -gt 0 ]; then
      json+=", "
    fi

    json+="\"$(json_escape "${values[$index]}")\""
  done

  json+="]"
  printf '%s' "$json"
}

mkdir -p "$(dirname "$output_path")"
if [ "${#host_addresses[@]}" -gt 0 ]; then
  host_addresses_json="$(json_array "${host_addresses[@]}")"
else
  host_addresses_json="[]"
fi

pairing_config_json="$(cat "$source_file")"

cat > "$output_path" <<EOF
{
  "schema": "ansight.pairing-config-document.v1",
  "config": $pairing_config_json,
  "discovery": {
    "schema": "ansight.discovery-hint.v1",
    "source": "developer-pairing-msbuild",
    "hostAddresses": $host_addresses_json,
    "hostName": "$(json_escape "$host_name")",
    "wifiName": "$(json_escape "$wifi_name")",
    "capturedAt": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  }
}
EOF

echo "Ansight developer pairing discovery: source=$source_file output=$output_path wifi=${wifi_name:-<unknown>} hostName=${host_name:-<unknown>} hostAddress=${host_address:-<unknown>} hostAddresses=${host_addresses[*]:-<unknown>}"
