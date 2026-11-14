#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# P1-16: compare the real native scanner with and without a network namespace.
set -euo pipefail
binary=$(realpath "${1:?Supply the published Linux binary}")
corpus=$(realpath "${2:-tests/corpus}")
report=$(realpath -m "${3:-artifacts/zero-egress}")
work=$(mktemp -d /tmp/gate-keeper-egress.XXXXXXXX)
trap 'rm -rf -- "$work"' EXIT
mkdir -p "$report"
cp "$binary" "$work/gatekeeper"
cp "$(dirname "$0")/Dockerfile" "$work/Dockerfile"
docker build --tag gate-keeper-zero-egress:test "$work"
count=0
for source in "$corpus"/*/repo; do
  [[ -d "$source" ]] || continue
  name=$(basename "$(dirname "$source")")
  root="$work/$name"
  mkdir -p "$root/.gatekeeper"
  cp -a "$source/." "$root/"
  cp "$(dirname "$0")/probe.pack.json" "$root/.gatekeeper/probe.json"
  printf 'schemaVersion = 1\n[repository]\nid = "synthetic/zero-egress"\n[packs]\nlocal = [".gatekeeper/probe.json"]\n' > "$root/.gatekeeper/gatekeeper.toml"
  printf 'gatekeeper-corpus-probe\n' > "$root/egress-probe.txt"
  for network in bridge none; do
    set +e
    docker run --rm --network "$network" --read-only --cap-drop ALL --security-opt no-new-privileges \
      --mount "type=bind,src=$root,dst=/scan,readonly" --workdir /scan \
      --env NO_COLOR=1 --env SOURCE_DATE_EPOCH=1793491200 \
      gate-keeper-zero-egress:test scan --json --evaluated-on 2026-11-01 \
      > "$report/$name.$network.json" 2> "$report/$name.$network.stderr"
    status=$?
    set -e
    # The mandatory probe must be a real violation, rather than an empty scan.
    if [[ "$status" != 1 ]]; then
      printf 'Corpus case %s (%s) returned %s; expected a completed scan with the probe finding.\n' "$name" "$network" "$status" >&2
      exit 1
    fi
    if grep -Eiq 'SocketException|HttpRequestException|Network is unreachable|Could not resolve|Connection refused' "$report/$name.$network.stderr"; then
      printf 'A network-related error occurred in corpus case %s.\n' "$name" >&2
      exit 1
    fi
    if ! grep -q '"ruleId": "local.egress.probe"' "$report/$name.$network.json"; then
      printf 'The scanner did not report the probe in corpus case %s.\n' "$name" >&2
      exit 1
    fi
  done
  cmp "$report/$name.bridge.json" "$report/$name.none.json"
  cmp "$report/$name.bridge.stderr" "$report/$name.none.stderr"
  count=$((count + 1))
done
if [[ "$count" == 0 ]]; then
  printf 'No corpus repositories were found; no zero-egress proof was produced.\n' >&2
  exit 1
fi
printf 'Verified %s corpus repositories: output, diagnostics and exit codes match with networking disabled.\n' "$count" | tee "$report/summary.txt"
