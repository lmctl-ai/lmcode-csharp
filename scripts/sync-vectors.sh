#!/usr/bin/env sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_dir=${LMCODE_CPP_DIR:-"$repo_dir/../lmcode-cpp"}/conformance/vectors
dest_dir="$repo_dir/conformance/vectors"
if [ ! -d "$source_dir" ]; then
  echo "vector source not found: $source_dir" >&2
  exit 1
fi
rm -rf "$dest_dir"
mkdir -p "$dest_dir"
cp -a "$source_dir"/. "$dest_dir"/
if git -C "${source_dir%/conformance/vectors}" rev-parse --short HEAD >/dev/null 2>&1; then
  git -C "${source_dir%/conformance/vectors}" rev-parse HEAD > "$dest_dir/SOURCE_COMMIT"
else
  printf '%s\n' 'unknown (source is not a git checkout)' > "$dest_dir/SOURCE_COMMIT"
fi
echo "synced vectors from $source_dir"
