# pkforge-sync-server

Keeps one [PKForge](https://github.com/sofianeelhor/PKForge) Bank in sync across your devices, such as
phones and handhelds. Every device keeps its full Bank and works offline; this server is where their
changes meet. Run it yourself with Docker.

Bank Sync is proposed for PKForge in a pull request. Until it is part of a PKForge release, build the app
from the `pr/bank-sync` branch of [YoruDev-Ryland/PKForge](https://github.com/YoruDev-Ryland/PKForge/tree/pr/bank-sync).

The server stores Pokémon as opaque bytes and never edits them. Conflicts (the same Pokémon edited on two
devices) are reported to the app, which asks you what to keep.

## Prerequisites

- To run it: Docker with Compose.
- To build or work on it: the .NET 10 SDK and Node.js 24 (see [CONTRIBUTING.md](CONTRIBUTING.md)).

PKHeX is included as a git submodule (`external/PKHeX`), so always clone with `--recurse-submodules`
(or run `git submodule update --init` in an existing clone). The build fails without it.

## Run it

1. Clone this repository with its submodule:

   ```sh
   git clone --recurse-submodules https://github.com/YoruDev-Ryland/pkforge-sync-server.git
   cd pkforge-sync-server
   ```

   In [docker-compose.yml](docker-compose.yml), set `PUBLIC_URL` to the address your devices use to reach
   this machine, for example `http://<your-server-ip>:8080`. (Until a prebuilt image is published, Compose
   builds it from the source here.)
2. Start it:

   ```sh
   docker compose up -d
   ```

3. In PKForge, open **Bank Sync**, choose **Connect to a server**, enter the same address and create your
   account. The first account on a server is its admin and can always sign up.
4. Add more devices by signing in on them, or with **Add a device** on a signed-in device (QR code or
   8-character code, valid 5 minutes).

> **Security note:** on a fresh server with no accounts, the first person to register can always do so
> (even with `ALLOW_REGISTRATION=false`) and becomes the admin. Create your own account (in the app, or with
> `docker exec pkforge-sync create-user you@example.com --admin`) before the server is reachable by anyone
> else.

## Settings

Set these as environment variables (all optional).

| Variable | Default | What it does |
|---|---|---|
| `PUBLIC_URL` | `http://localhost:8080` | Address devices and email links use to reach the server |
| `ALLOW_REGISTRATION` | `false` | Let anyone with the address create an account (the first account always can); once an admin changes it on the web Bank's Account page, that choice wins |
| `WEB_ROOT` | `wwwroot` beside the app | Folder with the built web app; set it only when running from source |
| `DATA_DIR` | `/data` in the Docker image; `data` (relative to the working directory) otherwise | Where the SQLite database and Pokémon data live |
| `DATABASE_PROVIDER` | `sqlite` | `sqlite` or `postgres` |
| `DATABASE_CONNECTION` | SQLite file in `DATA_DIR` | Database connection string (required for PostgreSQL) |
| `REQUIRE_EMAIL_VERIFICATION` | `false` | Block syncing until the account's email is confirmed |
| `TRUST_FORWARDED_HEADERS` | `false` | Trust `X-Forwarded-For`/`-Proto`; only behind your own reverse proxy |
| `RELEASED_RETENTION_DAYS` | `30` | How long a released Pokémon can be restored from the web Bank's Recently released (0 = releases are final) |
| `SERVER_NAME` | `PKForge Sync` | The name apps and the web Bank show for this server (at most 60 characters) |
| `SMTP_HOST` | (none) | Mail server for verification and password-reset emails; no host means no email |
| `SMTP_PORT` | `587` | Mail server port (465 uses TLS from the start) |
| `SMTP_USERNAME`, `SMTP_PASSWORD` | (none) | Mail server login |
| `SMTP_FROM` | `PKForge Sync <no-reply@localhost>` | Sender of the emails |

Every setting can also be given in ASP.NET Core form, such as `Sync__AllowRegistration=true`.

## Web Bank

The server also hosts a browser version of PKForge's Bank, served at the server's own address (for
example `http://localhost:8080/`). Sign in with your app account or with a pairing code from
**Add a device**. The Docker image includes it; nothing extra to set up.

To work on it, run the server on port 5099 and start the dev server (port 5173), which proxies `/v1` and
`/pkhex` to it:

```sh
git submodule update --init   # PKHeX is needed to build the server and for the art under /pkhex
ASPNETCORE_URLS=http://localhost:5099 dotnet run --project src/PKForge.Sync.Server --no-launch-profile
# in another terminal
cd web
npm ci
npm run dev
```

Before sending changes, run both type checks and the tests: `npm run check`, `npm run check:tests`,
`npm test` and, with Playwright browsers installed (`npx playwright install`), `npm run e2e`. `npm run build` writes the app to `web/dist`; `dotnet publish` does this for you
(skip it with `-p:SkipWebBuild=true`). To serve a local build from `dotnet run`, point the server at it:
`WEB_ROOT=$(pwd)/web/dist dotnet run --project src/PKForge.Sync.Server`.

## Admin commands

```sh
docker exec pkforge-sync list-users
docker exec pkforge-sync create-user misty@example.com          # prints a password
docker exec pkforge-sync create-user brock@example.com --admin
docker exec pkforge-sync reset-password ash@example.com         # prints a new password
docker exec pkforge-sync force-resync --all                      # after restoring a backup
```

Without email configured, `reset-password` is how a forgotten password is recovered.

## HTTPS and access from outside your network

Inside your home network plain HTTP is fine. To sync from outside, put the server behind a reverse proxy
that provides HTTPS (Caddy, Traefik, nginx, or a tunnel such as Tailscale), set `PUBLIC_URL` to the HTTPS
address, and set `TRUST_FORWARDED_HEADERS=true` so login rate limits see real client addresses. Never
expose plain HTTP to the internet: device keys and passwords would travel unencrypted.

The web Bank keeps one long-lived request open (`GET /v1/sync/events`, server-sent events) so changes from
other devices show up at once. The server marks it `X-Accel-Buffering: no` and sends a keep-alive every
25 seconds (`Sync__EventHeartbeatSeconds`); a proxy that buffers responses or closes idle connections sooner
than that only slows updates down to the page's regular check every two minutes.

## Backups

Everything lives in the `pkforge-sync-data` volume. Stop the container and copy the volume, or take a live
copy of the database with `sqlite3 /data/sync.db ".backup /data/backup.db"` and copy `/data/blobs` with it.
After restoring a backup, run `docker exec pkforge-sync force-resync --all`. Every device then re-downloads
the Bank at its next sync and uploads whatever the backup was missing, so nothing a device still holds is lost.

## One instance

Run one server per database. Sync writes are serialized inside the process, so several instances sharing one
database could interleave writes to the same Bank.

## PostgreSQL

```yaml
environment:
  DATABASE_PROVIDER: "postgres"
  DATABASE_CONNECTION: "Host=db;Database=pkforge_sync;Username=pkforge;Password=secret"
```

The server creates the database and its tables on first start.

## Extending

Deployments can register an `IAccountWritePolicy` to make accounts read-only, e.g. for quotas: a refused push
gets `403 writes_disabled` with the policy's reason, while reading and downloading keep working.

## Development

```sh
dotnet test tests/PKForge.Sync.Server.Tests   # Docker must be running for the PostgreSQL tests
dotnet run --project src/PKForge.Sync.Server
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full set of checks.

Database changes need a migration for each provider:

```sh
cd src/PKForge.Sync.Server
dotnet ef migrations add <Name> --context SqliteSyncDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add <Name> --context PostgresSyncDbContext --output-dir Data/Migrations/Postgres
```

## License

Copyright (C) 2026 PKForge Sync contributors

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
option) any later version (SPDX: `GPL-3.0-or-later`). See [LICENSE](LICENSE) for the full text.

Third-party components (PKHeX.Core and its images, PokeAPI data, fonts and packages) are listed with their
licenses in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Disclaimer

This project is not affiliated with, endorsed by or sponsored by Nintendo, Creatures Inc., GAME FREAK inc.
or The Pokémon Company. Pokémon and Pokémon character names are trademarks of their respective owners. No
game artwork is stored in this repository; the sprites, ball icons and box wallpapers the web Bank shows are
copied from PKHeX at build time.
