#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
installer_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec node "$installer_root/ci/native-release/install.mjs" "$@"
