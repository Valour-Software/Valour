#!/bin/bash
# Prints the name of the prebuilt WebKit release. It changes whenever the
# WebKit commit, the source preparation, or the build options change.
set -euo pipefail
cd "$(dirname "$0")"
commit=$(tr -d "[:space:]" < WebKit/COMMIT | cut -c1-12)
recipe=$(cat WebKit/COMMIT WebKit/fetch-source.sh Flatpak/webkit.yml | sha256sum | cut -c1-8)
echo "linux-webkit-$commit-$recipe"
