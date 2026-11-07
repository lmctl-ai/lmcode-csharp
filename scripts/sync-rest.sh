#!/usr/bin/env sh
# Copies the shared REST conformance suite (the opt-in `rest` std module) and pins it in rest-suite.lock.
#   scripts/sync-rest.sh            copy $LMCODE_SPEC_DIR/tests/rest-suite (default ../lmcode) and write rest-suite.lock
#   scripts/sync-rest.sh --verify   check conformance/rest-suite against the lock
# Portable digest: sha256 over rows "<sha256(file)>  <relative path>\n", paths sorted bytewise.
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
destination="$repo_dir/conformance/rest-suite"
lock="$repo_dir/rest-suite.lock"
digest() { (cd "$1" && find . -type f | sed 's|^\./||' | LC_ALL=C sort | while IFS= read -r f; do sha256sum "$f"; done) | sha256sum | cut -d' ' -f1; }
if [ "${1:-}" = "--verify" ]; then
  want=$(sed -n 's/^sha256=//p' "$lock")
  have=$(digest "$destination")
  [ -n "$want" ] && [ "$have" = "$want" ] || { echo "conformance/rest-suite digest $have differs from $want" >&2; exit 1; }
  echo "conformance/rest-suite matches $(sed -n 's/^commit=//p' "$lock")"
  exit 0
fi
spec=${LMCODE_SPEC_DIR:-"$repo_dir/../lmcode"}
commit=$(git -C "$spec" rev-parse HEAD)
rm -rf "$destination"
mkdir -p "$destination"
cp -a "$spec/tests/rest-suite"/. "$destination"/
printf 'repo=https://github.com/lmctl-ai/lmcode\ncommit=%s\nsha256=%s\n' "$commit" "$(digest "$destination")" > "$lock"
echo "synced REST suite from lmcode $commit"
