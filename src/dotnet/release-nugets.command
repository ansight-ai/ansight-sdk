#!/bin/zsh
set -uo pipefail

cd "${0:A:h}" || exit 1

if [[ -z "${ANSIGHT_NUGET_API_KEY:-}" && -f "${HOME}/.zshrc" ]]; then
  source "${HOME}/.zshrc"
fi

./release-nugets.sh "$@"
status=$?

echo
echo "release-nugets.sh exited with status ${status}."
echo "Press Return to close this window."
read -r _

exit "${status}"
