#!/bin/sh
# Runs an admin command named after this link, e.g. `reset-password you@example.com`. `docker exec` runs as
# root, so the command runs as the data folder's owner: files it creates stay writable by the server.
set -- dotnet /app/PKForge.Sync.Server.dll "$(basename "$0")" "$@"
if [ "$(id -u)" = "0" ] && [ -d "$DATA_DIR" ]; then
  exec setpriv --reuid="$(stat -c %u "$DATA_DIR")" --regid="$(stat -c %g "$DATA_DIR")" --clear-groups "$@"
fi
exec "$@"
