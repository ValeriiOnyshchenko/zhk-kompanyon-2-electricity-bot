#!/usr/bin/env bash
# Runs ON THE VM, piped in by the GitHub Actions deploy job.
# Expects the secrets (BOT_TOKEN, OWNER_USERNAME, DEYE_*) and IMAGE, GHCR_USER, GHCR_TOKEN
# to be defined before this script.
set -euo pipefail

APP_DIR="$HOME/zhkelectricitybot"
NAME=zhkelectricitybot

mkdir -p "$APP_DIR"
(
  umask 077
  {
    printf 'Bot__Token=%s\n'              "$BOT_TOKEN"
    printf 'Bot__OwnerUsername=%s\n'      "$OWNER_USERNAME"
    printf 'Deye__AppId=%s\n'             "$DEYE_APP_ID"
    printf 'Deye__AppSecret=%s\n'         "$DEYE_APP_SECRET"
    printf 'Deye__Email=%s\n'             "$DEYE_EMAIL"
    printf 'Deye__Password=%s\n'          "$DEYE_PASSWORD"
    printf 'Stations__0__StationId=%s\n'  "$DEYE_COMPLEX_STATION_ID"
    printf 'Stations__1__StationId=%s\n'  "$DEYE_HOME_STATION_ID"
  } > "$APP_DIR/bot.env"
)

echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
docker pull "$IMAGE"
docker logout ghcr.io

docker rm -f "$NAME" 2>/dev/null || true
docker run -d \
  --name "$NAME" \
  --restart unless-stopped \
  --env-file "$APP_DIR/bot.env" \
  -v zhkelectricitybot-data:/data \
  --log-opt max-size=10m --log-opt max-file=3 \
  "$IMAGE"

docker image prune -af >/dev/null
sleep 5
docker ps --filter "name=$NAME" --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
docker logs --tail 20 "$NAME"
