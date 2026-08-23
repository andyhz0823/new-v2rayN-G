#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
apply_patch() {
  local module="$1" patch="$2"
  git -C "$root/$module" apply --ignore-space-change --ignore-whitespace --whitespace=nowarn "$root/patches/$patch"
}
apply_patch v2rayN v2rayN-xboard.patch
apply_patch v2rayNG v2rayNG-xboard.patch