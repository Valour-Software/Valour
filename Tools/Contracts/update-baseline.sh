#!/bin/sh
# Replaces Valour/Tests/Contracts/baseline.json with the wire contract of a
# release, built from a temporary worktree at that release's tag. Use the
# oldest release that installed clients still run. The tag must include the
# wire contract tests. See Docs/ClientCompatibility.md.
#
# Usage: Tools/Contracts/update-baseline.sh v0.9.21
set -eu

if [ $# -ne 1 ]; then
  echo "Usage: $0 <release tag>" >&2
  exit 2
fi

TAG="$1"
ROOT="$(git rev-parse --show-toplevel)"
WORKTREE="$(mktemp -d)"

git -C "$ROOT" worktree add --detach "$WORKTREE" "$TAG"
trap 'git -C "$ROOT" worktree remove --force "$WORKTREE"' EXIT

if [ ! -f "$WORKTREE/Valour/Tests/Contracts/WireContractTests.cs" ]; then
  echo "$TAG does not include the wire contract tests, so its contract cannot be generated." >&2
  exit 1
fi

cd "$WORKTREE"
python3 Tools/Villages/restore-release-assets.py
VALOUR_UPDATE_WIRE_CONTRACT=1 dotnet test Valour/Tests/Valour.Tests.csproj \
  --filter "FullyQualifiedName~Valour.Tests.Contracts.WireContractTests.CurrentServerKeepsBaselineContract"

cp "$WORKTREE/Valour/Tests/Contracts/baseline.json" "$ROOT/Valour/Tests/Contracts/baseline.json"
echo "Updated baseline.json from $TAG. Clear accepted-breaks.txt, then run the contract tests."
