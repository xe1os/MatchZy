# MatchZy 0.8.15-refined.1.0.0

This fork is a drop-in replacement for MatchZy `0.8.15` and keeps the plugin
name, configuration paths, and command names unchanged.

## Fork base

- Upstream repository: <https://github.com/shobhit-pathak/MatchZy>
- Branch inspected: `dev`
- Exact base commit: `ef289d512766b89b0f7bf3088208ae88235e61fa`
- Upstream version reported by that commit: `0.8.15`

The local clone did not contain tag refs. The exact commit above is therefore
the reproducible fork base.

## Changes

- `.rt`, `.rethrow`, and `.throw` execute Valve's fixed,
  argument-free `sv_rethrow_last_grenade` command after MatchZy's existing
  practice-mode, connected-player, and grenade-history checks.
- Native rethrow is limited to once per player every 500 ms. Valve tracks the
  last grenade server-wide, so simultaneous practice by multiple players can
  rethrow another player's grenade.
- `.rethrowsmoke` and `.throwsmoke` retain MatchZy's per-player smoke history.
  Smoke recreation now creates a `smokegrenade_projectile` through the
  CounterStrikeSharp entity API instead of calling the brittle private smoke
  creation memory signature.
- Human player teleports use yaw-only body rotation, zero velocity, and then
  restore camera pitch/yaw separately. Practice, spawn correction, and coach
  teleports share the helper; bot teleports are unchanged.
- The project targets `.NET 10`, references `CounterStrikeSharp.API` `1.0.371`,
  and requires CounterStrikeSharp API version 371.

No command accepts or forwards arbitrary server-console text, and no RCON
functionality was added.

## Build

From the repository root:

```powershell
dotnet restore .\MatchZy.csproj
dotnet publish .\MatchZy.csproj -c Release --no-restore
```

`packages.lock.json` pins the complete dependency graph. CI and release builds
can use `dotnet restore .\MatchZy.csproj --locked-mode` to reject dependency
drift.

The publish artifacts are written to:

```text
bin/Release/net10.0/publish/
```

Copy the publish directory's contents to a directory named `MatchZy` under the
CounterStrikeSharp plugin directory. `CounterStrikeSharp.API.dll` and its PDB,
if present, should not be copied because the server supplies that API.

## UGREEN NAS installation

1. Stop the CS2 Docker container.
2. Back up the existing directory:
   `/volume1/GameServer/CS2/game/csgo/addons/counterstrikesharp/plugins/MatchZy`.
3. Preserve the existing MatchZy configuration under the server's `csgo/cfg`
   tree. This fork uses the same configuration layout.
4. Replace the contents of the plugin's `MatchZy` directory with the published
   fork artifacts. Do not install it beside the original plugin.
5. Start the container and confirm the log contains
   `[MatchZy 0.8.15-refined.1.0.0 LOADED]`.
6. Run `css_plugins list` and verify exactly one MatchZy instance is loaded.

## Server acceptance checks

- In practice mode, throw each grenade type and test `.rt` and `.rethrow`.
  Confirm the 500 ms cooldown ignores command spam.
- Test `.rethrowsmoke` separately and confirm it recreates a smoke, not a
  flashbang.
- Test `.last`, `.back`, `.loadpos`, `.spawn`, `.ctspawn`, `.tspawn`, saved
  lineups, and best/worst spawn commands while looking sharply up and down.
  Observe the player from a second client to verify body and leg orientation.
- Confirm camera pitch/yaw, position, zero velocity, crouch state, and common
  practice commands still behave correctly.
- Check logs for exceptions through map changes and a plugin reload.
