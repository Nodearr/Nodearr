#!/bin/sh
# Starts Nodearr as the user PUID:PGID, so that what it writes to /config and /downloads belongs
# to the same user as Sonarr and Radarr, which have to read and move those files.
set -e

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"

if [ "$(id -u)" != "0" ]; then
    # Started with --user. Nothing to hand over.
    exec "$@"
fi

mkdir -p /config /downloads

# /config is small, and has to be Nodearr's all the way down. /downloads can be large and is
# shared, so only the folder itself is touched.
chown -R "$PUID:$PGID" /config
chown "$PUID:$PGID" /downloads

exec gosu "$PUID:$PGID" "$@"
