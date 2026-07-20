#!/bin/sh
# Blue/green auto-deployer for a Valour node.
# Run periodically (see valour-autodeploy.timer); exits quickly when the
# remote image digest matches what is already deployed.
set -eu

# Default to the directory this script lives in (the deploy files sit alongside
# it). Avoids depending on $HOME, which a root-run systemd oneshot may not set.
VALOUR_HOME="${VALOUR_HOME:-$(cd "$(dirname "$0")" && pwd)}"
cd "$VALOUR_HOME"

exec 9>"$VALOUR_HOME/deploy.lock"
flock -n 9 || exit 0

TAG="ghcr.io/valour-software/valour:main-latest"

REMOTE_DIGEST="$(docker buildx imagetools inspect "$TAG" | awk '/Digest:/ { print $2; exit }')"
CURRENT_DIGEST="$(cat current.digest 2>/dev/null || true)"

if [ "$REMOTE_DIGEST" = "$CURRENT_DIGEST" ]; then
  echo "Already running $REMOTE_DIGEST"
  exit 0
fi

ACTIVE="$(cat active-color 2>/dev/null || echo blue)"

if [ "$ACTIVE" = "blue" ]; then
  NEXT="green"
else
  NEXT="blue"
fi

# Stable per-color worker id (blue=0, green=1) so a blue/green overlap never
# runs two writers sharing a worker id.
worker_id() {
  if [ "$1" = "green" ]; then echo 1; else echo 0; fi
}
NEXT_WORKERID="$(worker_id "$NEXT")"
ACTIVE_WORKERID="$(worker_id "$ACTIVE")"

IMAGE="ghcr.io/valour-software/valour@$REMOTE_DIGEST"

echo "Deploying $IMAGE"
echo "Active: $ACTIVE"
echo "Next: $NEXT"

VALOUR_IMAGE="$IMAGE" VALOUR_COLOR="$NEXT" VALOUR_WORKERID="$NEXT_WORKERID" \
  docker compose -f docker-compose.app.yml -p "valour-$NEXT" up -d --force-recreate

for i in $(seq 1 60); do
  if docker run --rm --network valour-network curlimages/curl:latest \
    -fsS "http://valour-$NEXT:5000/healthz" | grep -qx ready; then
    echo "$NEXT is healthy"
    break
  fi

  if [ "$i" = "60" ]; then
    echo "$NEXT failed health check"
    docker logs --tail=200 "valour-node-$NEXT" || true
    exit 1
  fi

  sleep 2
done

# Warmup grace: /healthz flips to "ready" before the membership/presence caches
# and the federation hub are hot. Taking traffic immediately produces transient
# 404s (e.g. GET /api/members/...) for the first reconnecting clients.
sleep 8

# Point nginx at the new color and reload.
cat > nginx/upstream.conf <<EOF
upstream valour_backend {
    server valour-$NEXT:5000;
}
EOF

docker exec valour-nginx nginx -t
docker exec valour-nginx nginx -s reload

# Traffic is now on $NEXT. Record state immediately so any hiccup during the
# old-color teardown below can never leave active-color/current.digest stale
# (which would make the next run redeploy on top of the wrong color).
echo "$NEXT" > active-color
echo "$REMOTE_DIGEST" > current.digest

# Drain: let in-flight requests finish and SignalR clients reconnect to $NEXT
# before the old container disappears, so REST calls don't hit a dead upstream
# during the swap (the CORS/"Failed to fetch" storm seen client-side).
sleep 15

# Tear down the old color — this is what makes it real blue/green. The env vars
# are REQUIRED: without them compose cannot interpolate image/container_name in
# docker-compose.app.yml, matches nothing, and the old container survives every
# cutover (leaving two live nodes on the shared DB/Redis). No "|| true": a real
# failure here must be visible, not silently swallowed.
if ! VALOUR_IMAGE="$IMAGE" VALOUR_COLOR="$ACTIVE" VALOUR_WORKERID="$ACTIVE_WORKERID" \
    docker compose -f docker-compose.app.yml -p "valour-$ACTIVE" down; then
  echo "WARNING: failed to tear down old color $ACTIVE - it may still be running"
fi

echo "Deployment complete: $NEXT @ $REMOTE_DIGEST"
