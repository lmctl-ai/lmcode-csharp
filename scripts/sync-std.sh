#!/usr/bin/env sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_root=${LMCODE_CPP_DIR:-"$repo_dir/../lmcode-cpp"}
source="$source_root/std/lmcode-std.schema.json"
destination="$repo_dir/std/lmcode-std.schema.json"
lock="$repo_dir/std/std.lock"
if [ ! -f "$source" ]; then echo "canonical std schema not found: $source" >&2; exit 1; fi
if [ "${1:-}" = "--verify" ]; then
  test "$(sha256sum "$destination" | cut -d' ' -f1)" = "$(sed -n 's/^sha256=//p' "$lock")" || { echo "std schema digest differs from std.lock" >&2; exit 1; }
  cmp -s "$source" "$destination" || { echo "std schema differs from C++ source" >&2; exit 1; }
  echo "canonical std schema matches $(sed -n 's/^source_commit=//p' "$lock")"
  exit 0
fi
cp "$source" "$destination"
printf 'source_commit=%s\nsha256=%s\n' "$(git -C "$source_root" rev-parse HEAD)" "$(sha256sum "$destination" | cut -d' ' -f1)" > "$lock"
echo "synced canonical std schema from $source"
