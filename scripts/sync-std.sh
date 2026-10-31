#!/usr/bin/env sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_root=${LMCODE_CPP_DIR:-"$repo_dir/../lmcode-cpp"}
source="$source_root/std/lmcode-std.schema.json"
destination="$repo_dir/std/lmcode-std.schema.json"
lock="$repo_dir/std/std.lock"
if [ "${1:-}" = "--verify" ]; then
  test "$(sha256sum "$destination" | cut -d' ' -f1)" = "$(sed -n 's/^sha256=//p' "$lock")" || { echo "std schema digest differs from std.lock" >&2; exit 1; }
  test -f "$repo_dir/std/lmcode-std.docs.json" || { echo "canonical std docs missing" >&2; exit 1; }
    test "$(sha256sum "$repo_dir/std/lmcode-std.docs.json" | cut -d' ' -f1)" = "$(sed -n 's/^docs_sha256=//p' "$lock")" || { echo "std docs digest differs from std.lock" >&2; exit 1; }
  echo "canonical std schema matches $(sed -n 's/^source_commit=//p' "$lock")"
  exit 0
fi
commit=$(git -C "$source_root" log -1 --format=%H -- std/lmcode-std.schema.json)
[ -n "$commit" ] || { echo "canonical std schema has no committed revision" >&2; exit 1; }
mkdir -p "$repo_dir/std"
git -C "$source_root" show "$commit:std/lmcode-std.schema.json" > "$destination"
git -C "$source_root" show "$commit:std/lmcode-std.docs.json" > "$repo_dir/std/lmcode-std.docs.json"
printf 'source_commit=%s\nsha256=%s\n' "$commit" "$(sha256sum "$destination" | cut -d' ' -f1)" > "$lock"
printf 'docs_sha256=%s\n' "$(sha256sum "$repo_dir/std/lmcode-std.docs.json" | cut -d' ' -f1)" >> "$lock"
echo "synced canonical std schema from $source"
