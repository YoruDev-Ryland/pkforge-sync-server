#!/bin/sh
# Runs an admin command named after this link, e.g. `reset-password you@example.com`.
exec dotnet /app/PKForge.Sync.Server.dll "$(basename "$0")" "$@"
