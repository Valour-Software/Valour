#!/bin/bash
# Checks out the pinned WebKit commit into the given directory and turns WebRTC
# on. Only that commit's Source, Tools, and top-level build files are fetched.
# Running it again with the same commit only reapplies the change.
set -euo pipefail

DEST=${1:?usage: fetch-source.sh <directory>}
COMMIT=${WEBKIT_COMMIT:-$(cat "$(dirname "$0")/COMMIT")}

if [ ! -d "$DEST/.git" ]; then
    git init -q "$DEST"
    git -C "$DEST" remote add origin https://github.com/WebKit/WebKit.git
    # Tests and other large directories are not needed to build.
    git -C "$DEST" sparse-checkout set --cone Source Tools
fi
if [ "$(git -C "$DEST" rev-parse HEAD 2>/dev/null)" != "$COMMIT" ]; then
    git -C "$DEST" fetch -q --depth 1 --filter=blob:none origin "$COMMIT"
    git -C "$DEST" checkout -q --force FETCH_HEAD
fi

# Release builds leave WebRTC out unless every experimental feature is on.
sed -i 's/WEBKIT_OPTION_DEFAULT_PORT_VALUE(ENABLE_WEB_RTC PRIVATE ${ENABLE_EXPERIMENTAL_FEATURES})/WEBKIT_OPTION_DEFAULT_PORT_VALUE(ENABLE_WEB_RTC PRIVATE ON)/' \
    "$DEST/Source/cmake/OptionsGTK.cmake"
grep -q "ENABLE_WEB_RTC PRIVATE ON" "$DEST/Source/cmake/OptionsGTK.cmake"
