#!/bin/sh
# Started as root, the container only makes the data folder usable (a bind mount Docker just created belongs
# to root), then runs the server as PUID:PGID (default: the image's app user). Started as any other user
# (compose `user:`), it runs the server as that user and changes nothing.
set -e
if [ "$(id -u)" = "0" ]; then
  uid="${PUID:-1654}"
  gid="${PGID:-1654}"
  mkdir -p "$DATA_DIR"
  find "$DATA_DIR" \( ! -user "$uid" -o ! -group "$gid" \) -exec chown "$uid:$gid" {} +
  exec setpriv --reuid="$uid" --regid="$gid" --clear-groups "$@"
fi
exec "$@"
