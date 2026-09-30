#!/bin/bash
# Builds WebKitGTK (GTK 3, libsoup 3, API 4.1) with WebRTC enabled on Ubuntu and
# installs it under /build/stage/opt/valour-webkit. Mount a volume at /build to
# keep the checkout and build tree between runs; an interrupted build resumes.
set -euo pipefail

PREFIX=/opt/valour-webkit
cd /build
before=$(git -C WebKit rev-parse HEAD 2>/dev/null || true)
/fetch-source.sh WebKit
if [ "$before" != "$(git -C WebKit rev-parse HEAD)" ]; then
    rm -rf out
fi

mkdir -p out
cd out
if [ ! -f build.ninja ]; then
    cmake -G Ninja ../WebKit \
        -DPORT=GTK -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$PREFIX \
        -DUSE_GTK4=OFF \
        -DENABLE_DOCUMENTATION=OFF -DENABLE_INTROSPECTION=OFF -DENABLE_MINIBROWSER=OFF \
        -DENABLE_SPEECH_SYNTHESIS=OFF -DUSE_FLITE=OFF -DENABLE_GAMEPAD=OFF -DENABLE_WEBDRIVER=OFF \
        -DENABLE_BUBBLEWRAP_SANDBOX=OFF -DUSE_LIBBACKTRACE=OFF -DUSE_LIBHYPHEN=OFF -DUSE_JPEGXL=OFF \
        -DUSE_SYSTEM_SYSPROF_CAPTURE=OFF -DENABLE_API_TESTS=OFF -DENABLE_TOOLS=OFF
fi
grep -q "#define USE_LIBWEBRTC 1" cmakeconfig.h

# Each WebCore JavaScript bindings unit needs more than 2 GB to compile.
ninja -j"${JOBS:-4}"
rm -rf /build/stage
DESTDIR=/build/stage ninja install
strip --strip-unneeded /build/stage$PREFIX/lib/*.so.*
echo "Installed to /build/stage$PREFIX"
