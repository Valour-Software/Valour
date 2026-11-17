#!/bin/sh
# NVIDIA's driver and WebKitGTK disagree about explicit sync on Wayland, which
# leaves the window blank or crashes it at startup. Only the NVIDIA driver reads
# this variable, so other graphics cards are unaffected. Set it to 0 before
# launching to turn explicit sync back on.
export __NV_DISABLE_EXPLICIT_SYNC="${__NV_DISABLE_EXPLICIT_SYNC:-1}"
exec /app/lib/valour/Valour "$@"
