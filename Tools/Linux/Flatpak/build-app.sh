#!/bin/bash
# Builds Valour-linux-<arch>.flatpak from a published app and a prebuilt WebKit.
# Runs inside ghcr.io/flathub-infra/flatpak-github-actions, started with
# --privileged, after webkit.tar.zst and valour/ are placed beside this script.
set -euo pipefail

OUT=${1:?usage: build-app.sh <output directory>}
HERE=$(cd "$(dirname "$0")" && pwd)
ARCH=$(flatpak --default-arch)
# flatpak-builder moves directories inside its work directory, which fails on
# a container's overlay filesystem, so the work directory sits in the checkout.
WORK="$HERE/.build-app"
clean() { mkdir -p "$WORK" && find "$WORK" -mindepth 1 -maxdepth 1 -exec rm -rf {} +; }
clean
trap clean EXIT

mkdir -p "$OUT"
cd "$HERE"
flatpak-builder --disable-rofiles-fuse --force-clean --repo="$WORK/repo" --state-dir="$WORK/state" "$WORK/build" gg.valour.Valour.yml
flatpak build-bundle --arch="$ARCH" --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo \
    "$WORK/repo" "$OUT/Valour-linux-$ARCH.flatpak" gg.valour.Valour
echo "Wrote $OUT/Valour-linux-$ARCH.flatpak"
