#!/usr/bin/env sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
source_root=${LMCODE_CPP_DIR:-"$repo_dir/../lmcode-cpp"}/conformance
core_source="$source_root/vectors"
catalog_source="$source_root/stdlib-vectors"
core_dest="$repo_dir/conformance/vectors"
catalog_dest="$repo_dir/conformance/stdlib-vectors"
if [ ! -d "$core_source" ] || [ ! -d "$catalog_source" ]; then
  echo "vector source not found under: $source_root" >&2
  exit 1
fi
rm -rf "$core_dest" "$catalog_dest"
mkdir -p "$core_dest" "$catalog_dest"
cp -a "$core_source"/. "$core_dest"/
cp -a "$catalog_source"/. "$catalog_dest"/
if git -C "${source_root%/conformance}" rev-parse --short HEAD >/dev/null 2>&1; then
  git -C "${source_root%/conformance}" rev-parse HEAD > "$core_dest/SOURCE_COMMIT"
  git -C "${source_root%/conformance}" rev-parse HEAD > "$catalog_dest/SOURCE_COMMIT"
else
  printf '%s\n' 'unknown (source is not a git checkout)' > "$core_dest/SOURCE_COMMIT"
  printf '%s\n' 'unknown (source is not a git checkout)' > "$catalog_dest/SOURCE_COMMIT"
fi
echo "synced core and catalog vectors from $source_root"
