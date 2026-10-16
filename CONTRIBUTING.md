# Contributing

Thanks for helping. Keep changes small and focused, and include tests for what you change.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 24](https://nodejs.org/) (for the web Bank in `web/`)
- Docker, running, for the PostgreSQL tests (they use Testcontainers) and for building the image

PKHeX is a git submodule, so clone with it:

```sh
git clone --recurse-submodules https://github.com/YoruDev-Ryland/pkforge-sync-server.git
# or, in an existing clone:
git submodule update --init
```

## Running the checks

Server:

```sh
dotnet test tests/PKForge.Sync.Server.Tests
```

Web Bank:

```sh
cd web
npm ci
npm run check          # type check the app
npm run check:tests    # type check the tests
npm test               # unit tests (Vitest)
npm run e2e            # end-to-end tests (Playwright; run `npx playwright install` once first)
```

The end-to-end tests start their own server on port 5199 with a throwaway data folder. Screenshot tests
compare against `web/tests/e2e/__screenshots__`; update them only for intended visual changes.

CI runs the server tests, the web type checks and unit tests, and a Docker build.

## Database changes

Every schema change needs a migration for **both** providers, SQLite and PostgreSQL, generated with the same
name:

```sh
cd src/PKForge.Sync.Server
dotnet ef migrations add <Name> --context SqliteSyncDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add <Name> --context PostgresSyncDbContext --output-dir Data/Migrations/Postgres
```

`dotnet tool restore` installs the `dotnet ef` version pinned in `dotnet-tools.json`.

## Pull requests

- One topic per pull request; describe what changed and why.
- Add or update tests alongside the code.
- Make sure all checks above pass.
- By contributing you agree that your contribution is licensed under GPL-3.0-or-later, the project's license.
