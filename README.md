# v2rayN Cloudflare Auto Switch Companion

This is a sidecar plugin package for v2rayN. It is not loaded by v2rayN in-process and does not modify v2rayN's subscription, node parsing, speed test, or core config business logic.

## What It Does

- Stores multiple Cloudflare API token rules.
- Maps each token/Worker to a v2rayN subscription group name.
- Checks today's Worker request count from Cloudflare GraphQL Analytics.
- When the current group's Worker exceeds the threshold, switches to another configured group that is still below threshold.
- Runs v2rayN's existing mixed latency/speed test flow through `ServiceLib`.
- Selects the best node by lowest delay, then highest speed.
- Writes v2rayN's speed test URL to:

```text
https://cdn.cloudflare.steamstatic.com/steam/apps/256843155/movie_max.mp4
```

## How To Use

Build requirements:

- .NET SDK 10.0 or newer with Windows Desktop workload support.
- A v2rayN source checkout that matches the target v2rayN release.

1. Build or publish the companion with `.\build-plugin.ps1`.

   If this package is no longer next to the v2rayN source checkout, pass the source path explicitly:

   ```powershell
   .\build-plugin.ps1 -V2rayNSourceDirectory "C:\path\to\v2rayN\v2rayN"
   ```
2. Install it into the v2rayN directory:

```powershell
.\install-plugin.ps1 -V2rayNDirectory "C:\path\to\v2rayN"
```

The same install command works from either the source directory or the compiled package directory.

3. Run `v2rayN.AutoSwitchCompanion.exe`.
4. Add one row per Cloudflare Worker:
   - `GroupName` must exactly match the v2rayN subscription group name.
   - `ApiToken` needs Cloudflare Analytics read permission.
   - `ThresholdRequests` can be `90000` or `95000`.
5. Click `Check now` to test, or `Start monitor` for periodic checks.

## Important Notes

- v2rayN has no official plugin loader or external reload API. This package is therefore a sidecar companion tool.
- By default it restarts `v2rayN.exe` after changing the selected node so the new config is active.
- Tokens are currently stored in `autoswitch-companion.json` next to the companion executable. Keep that file private.
- The sidecar must run from the `v2rayN.exe` directory because v2rayN's `ServiceLib` resolves `guiConfigs` relative to the process base directory.
- The compiled package includes `ServiceLib.dll` and related managed dependencies because some v2rayN Windows releases are single-file builds and do not ship those DLLs separately.
- Build the sidecar against the same v2rayN source version as the target v2rayN release to avoid `ServiceLib` API mismatches.
- The build only needs the v2rayN source path for compile-time references. The install step copies only the companion files and refuses to overwrite v2rayN-owned binaries.
