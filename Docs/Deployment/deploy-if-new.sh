#!/bin/sh
# Blue/green auto-deployer for a Valour node.
# Run periodically (see valour-autodeploy.timer); exits quickly when the
# remote image digest matches what is already deployed.
set -eu

VALOUR_HOME="${VALOUR_HOME:-$HOME/valour}"
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

IMAGE="ghcr.io/valour-software/valour@$REMOTE_DIGEST"

echo "Deploying $IMAGE"
echo "Active: $ACTIVE"
echo "Next: $NEXT"

VALOUR_IMAGE="$IMAGE" VALOUR_COLOR="$NEXT" \
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

cat > nginx/upstream.conf <<EOF
upstream valour_backend {
    server valour-$NEXT:5000;
}
EOF

docker exec valour-nginx nginx -t
docker exec valour-nginx nginx -s reload

docker compose -f docker-compose.app.yml -p "valour-$ACTIVE" down || true

echo "$NEXT" > active-color
echo "$REMOTE_DIGEST" > current.digest

echo "Deployment complete: $NEXT @ $REMOTE_DIGEST"
