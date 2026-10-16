# Third-party notices

PKForge Sync Server is licensed under GPL-3.0-or-later (see [LICENSE](LICENSE)). It builds on the
third-party components below, each under its own license.

## Trademarks

PKForge Sync is not affiliated with, endorsed by or sponsored by Nintendo, Creatures Inc., GAME FREAK inc. or
The Pokémon Company. Pokémon and Pokémon character names are trademarks of their respective owners. No game
artwork is stored in this repository; sprites, ball icons and box wallpapers are copied from PKHeX at build
time.

## PKHeX.Core

- Source: [kwsch/PKHeX](https://github.com/kwsch/PKHeX), used through the
  [PKForge-PKHeX](https://github.com/sofianeelhor/PKForge-PKHeX) fork as the `external/PKHeX` git submodule.
- License: GNU General Public License v3.0 (the same text as [LICENSE](LICENSE)).
- Used to read Pokémon files for the web Bank's inspector.

## PKHeX images

- Pokémon sprites, ball icons and box wallpapers, served under `/pkhex`, are copied at build time from the
  PKHeX submodule (`PKHeX.Drawing.PokeSprite` and `PKHeX.Drawing.Misc`) and distributed under PKHeX's license
  (GPLv3).
- The shiny sprites in PKHeX come from [pokesprite](https://github.com/msikma/pokesprite) (MIT License,
  by Michiel Sikma and contributors); the artwork itself remains subject to the
  trademark notice above.

## PokeAPI data

`src/PKForge.Sync.Server/Resources/dexfacts.tsv.gz` (move names, types, power and descriptions) is generated
from [PokeAPI](https://github.com/PokeAPI/pokeapi) data, licensed under the BSD 3-Clause License:

```
Copyright (c) 2014, Paul Hallett and PokeAPI contributors
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Fonts

The web app bundles these fonts through [Fontsource](https://fontsource.org/) (`@fontsource/*` npm packages),
each under the SIL Open Font License 1.1. The full license text ships with the web app at
`/licenses/OFL.txt` (source: [web/public/licenses/OFL.txt](web/public/licenses/OFL.txt)).

| Font | Copyright |
|---|---|
| Hanken Grotesk | Copyright 2021 The Hanken Grotesk Project Authors (https://github.com/marcologous/hanken-grotesk) |
| IBM Plex Mono | Copyright 2017 IBM Corp. All rights reserved. |
| M PLUS Rounded 1c | Copyright 2016 The Rounded M+ Project Authors. |

## npm runtime dependencies (web app)

| Package | License |
|---|---|
| [svelte](https://github.com/sveltejs/svelte) | MIT |
| [fflate](https://github.com/101arrowz/fflate) | MIT |

## NuGet runtime dependencies (server)

| Package | License |
|---|---|
| MailKit, MimeKit | MIT |
| BouncyCastle.Cryptography (via MimeKit) | MIT |
| Microsoft.EntityFrameworkCore (Sqlite) | MIT |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License |
| SQLitePCLRaw (bundle, provider, e_sqlite3) | Apache-2.0 (SQLite itself is public domain) |
| System.Security.Cryptography.Pkcs | MIT |

The .NET runtime and ASP.NET Core (MIT) are provided by the base Docker image.

Each package's full license text is included in its package and at its project page.
