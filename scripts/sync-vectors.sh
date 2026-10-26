#!/usr/bin/env sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_root=${LMCODE_CPP_DIR:-"$repo_dir/../lmcode-cpp"}
commit=$(git -C "$source_root" rev-parse HEAD)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
# Always read committed bytes: an in-progress upstream vector cannot enter a pin.
git -C "$source_root" archive --format=tar "$commit" conformance/vectors conformance/stdlib-vectors > "$scratch/vectors.tar"
tar -xf "$scratch/vectors.tar" -C "$scratch"
for suite in vectors stdlib-vectors; do
  destination="$repo_dir/conformance/$suite"
  rm -rf "$destination"
  mkdir -p "$destination"
  cp -a "$scratch/conformance/$suite"/. "$destination"/
  printf '%s\n' "$commit" > "$destination/SOURCE_COMMIT"
done
echo "synced committed core and catalog vectors at $commit"
