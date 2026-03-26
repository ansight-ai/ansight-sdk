#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")"
./pack-nugets.sh "$@"
