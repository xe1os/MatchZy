# MatchZy 0.8.15-refined.1.0.0

This fork is a drop-in replacement for MatchZy `0.8.15` and keeps the plugin
name, configuration paths, and command names unchanged.

## Fork base

- Upstream repository: <https://github.com/shobhit-pathak/MatchZy>
- Branch inspected: `dev`
- Exact base commit: `ef289d512766b89b0f7bf3088208ae88235e61fa`
- Upstream version reported by that commit: `0.8.15`

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
