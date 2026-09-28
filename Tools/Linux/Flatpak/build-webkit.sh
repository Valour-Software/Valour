#!/bin/bash
# Builds WebKitGTK for the Flatpak and writes webkit-<arch>.tar.zst to the
# output directory. Runs inside ghcr.io/flathub-infra/flatpak-github-actions,
# started with --privileged. The work directory keeps flatpak-builder's cache,
# so a rerun after an interruption reuses compiled objects.
set -euo pipefail

WORK=${1:?usage: build-webkit.sh <work directory> <output directory>}
OUT=${2:?usage: build-webkit.sh <work directory> <output directory>}
HERE=$(cd "$(dirname "$0")" && pwd)
ARCH=$(flatpak --default-arch)

mkdir -p "$WORK" "$OUT"
"$HERE/../WebKit/fetch-source.sh" "$WORK/webkit-src"
cp "$HERE/webkit.yml" "$WORK/"
cd "$WORK"
flatpak-builder --disable-rofiles-fuse --ccache --jobs="${JOBS:-3}" --force-clean builddir webkit.yml
rm -f builddir/files/manifest.json
tar -C builddir/files -I 'zstd -19 -T0' -cf "$OUT/webkit-$ARCH.tar.zst" .
(cd "$OUT" && sha256sum "webkit-$ARCH.tar.zst" > "webkit-$ARCH.tar.zst.sha256")
echo "Wrote $OUT/webkit-$ARCH.tar.zst"
