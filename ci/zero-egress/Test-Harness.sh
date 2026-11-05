#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Negative tests keep the proof from accepting a tool failure or changed output.
set -euo pipefail
script=$(realpath "$(dirname "$0")/Test-ZeroEgress.sh")
work=$(mktemp -d /tmp/gate-keeper-egress-test.XXXXXXXX)
trap 'rm -rf -- "$work"' EXIT
mkdir -p "$work/bin" "$work/corpus/example/repo"
printf 'fixture\n' > "$work/binary"
cat > "$work/bin/docker" <<'SHIM'
#!/usr/bin/env bash
if [[ "$1" == build ]]; then exit 0; fi
network=bridge
for ((i=1; i<=$#; i++)); do
  if [[ "${!i}" == --network ]]; then j=$((i + 1)); network=${!j}; fi
done
if [[ "$MODE" == tool-error ]]; then exit 2; fi
if [[ "$MODE" == empty ]]; then printf '{"findings":[]}\n'; exit 1; fi
if [[ "$MODE" == network-error ]]; then printf 'Network is unreachable\n' >&2; fi
if [[ "$MODE" == changed && "$network" == none ]]; then printf 'changed\n'; fi
if [[ "$MODE" == diagnostic && "$network" == none ]]; then printf 'changed diagnostic\n' >&2; fi
printf '{"ruleId": "local.egress.probe"}\n'
exit 1
SHIM
chmod +x "$work/bin/docker"
export PATH="$work/bin:$PATH"
for mode in tool-error empty changed diagnostic network-error; do
  if MODE="$mode" bash "$script" "$work/binary" "$work/corpus" "$work/report-$mode" > /dev/null 2>&1; then
    printf 'The proof incorrectly accepted %s.\n' "$mode" >&2
    exit 1
  fi
done
MODE=matching bash "$script" "$work/binary" "$work/corpus" "$work/report-pass" > /dev/null
mkdir "$work/empty-corpus"
if MODE=matching bash "$script" "$work/binary" "$work/empty-corpus" "$work/report-empty" > /dev/null 2>&1; then
  printf 'The proof incorrectly accepted an empty corpus.\n' >&2
  exit 1
fi
printf 'Zero-egress harness rejection and success tests passed.\n'
