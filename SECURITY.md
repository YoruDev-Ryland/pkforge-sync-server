# Security policy

## Reporting a vulnerability

Please report security problems privately through GitHub Security Advisories: open the repository's
**Security** tab and choose **Report a vulnerability**. Do not open a public issue, pull request or
discussion for a suspected vulnerability.

Include what you found, how to reproduce it, the version or commit you tested and the impact you expect.
You will get a reply in the advisory thread; once a fix is available the advisory is published with credit,
unless you prefer to stay anonymous.

## Supported versions

Only the latest `main` (and images built from it) receives security fixes. Please check that a problem still
exists on the latest `main` before reporting it.

## Scope

In scope:

- The sync server in this repository: its HTTP API, authentication (passwords, device keys, pairing codes,
  web sessions), account isolation, rate limits and the admin commands.
- The web Bank served by the server (`web/`).
- The Docker image and Compose file as shipped here.

Out of scope:

- Deployments exposed over plain HTTP, or with `TRUST_FORWARDED_HEADERS=true` without a reverse proxy you
  control (the README warns against both).
- The first-account behavior on a fresh server (the first registration is always allowed and becomes the
  admin); this is documented and intended.
- Vulnerabilities in PKHeX, the PKForge app or third-party packages; report those to their projects
  (a report showing how one affects this server is welcome here).
- Denial of service by sheer traffic volume.
