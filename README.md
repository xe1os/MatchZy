# MatchZy-Refined

MatchZy-Refined is a fork of
[shobhit-pathak/MatchZy](https://github.com/shobhit-pathak/MatchZy) that refines
the original project.

This fork includes all functionality available in the forked repository and
adds the following focused features, fixes, and changes.

## Features

- Added `.menu` for a per-player center-screen configuration menu covering practice startup, bot settings, player settings and saved positions, spawn controls, and utility actions. Use `W`/`S` to navigate and `A`/`D` to change values or select actions.
- Added `.grt` as an explicit practice-mode command for CS2's server-global grenade rethrow.
- Added `.slp` to save the player's current position and view direction, `.tlp` to return to it, and `.dlp` to delete it.
- Added interactive T and CT spawn outlines in practice mode; stand inside a flat gold square and press `E` to teleport to that exact spawn. The outlines remain visible above shallow water, and `.showspawns` and `.hidespawns` control their visibility.
- Added `.sbp <name>`, `.lbp <name>`, and `.dbp <name>` for saving, restoring, and deleting named, multi-word bot-position setups with their teams, aim directions, and crouch states.
- Added `.changemap <map-name>` so any player can switch to a validated map, with or without the `de_` prefix.
- Added `.botshoot <true/false>` and `.botreactiontime <0-1000>` to turn practice bots into stationary, visibility-aware opponents and configure their firing delay.
- Added `.botrespawn <true/false>` and `.botlifereg <true/false>` to control practice-bot respawning and health regeneration independently of human players.
- Added per-player `.liferegon <true/false>` health regeneration for human players in practice mode, enabled by default and individually opt-out.

## BugFixes

- `.rt`, `.rethrow`, and `.throw` now use the requesting player's own utility history during multiplayer practice sessions.
- Smoke and HE grenade rethrows now recreate the correct projectile, follow the recorded trajectory, and detonate normally.
- Player teleports no longer apply camera pitch or roll to the player model.
- Nade deletion commands now resolve multi-word lineup queries consistently with `.loadnade`.
- Spawn commands and markers now use each map's primary competitive T and CT spawn groups, with higher-priority fallbacks used only when needed to provide five spawns per side.

## Changes

- Configuration-menu navigation uses CS2 movement actions, so the keys also move the player. Arrow keys work only when the player has bound them to the corresponding movement actions.
- The configuration menu omits actions that cannot currently be used and keeps its five-row viewport compact so available options remain visible.
- The configuration menu suppresses CS2's periodic white center-HUD fade while open and uses compact saved-position labels without redundant state suffixes.
- Rethrow commands use per-player cooldowns and clear the corresponding state when a player disconnects.
- Practice rounds continue until the configured timer expires when the final enemy bot dies, regardless of the bot-respawn setting.
- `.clear` also removes dropped weapons, grenades, and defuse kits while preserving the C4 and player-held equipment.
- Human players retain full Kevlar during practice mode without preventing health damage.
- Bot-position presets load more reliably when replacing existing bots and remain usable while bot respawning is disabled.
- `.bot` always gives a newly added practice bot its initial live spawn; `.botrespawn` controls only subsequent death respawns.
- Human practice deaths respawn at the player's `.slp` location, or at a random competitive spawn for the player's current side when no location is saved.
- `.god` now uses real per-player damage immunity and a safe `int.MaxValue / 2` health target; `.liferegon` preserves that target while God mode is enabled.
- Enabled `.liferegon` and `.botlifereg` restore injured players and bots on a fixed 100 ms cadence, including during continuous damage, while skipping entities already at maximum health.

## Upstream project

The original project and its history are available at
[shobhit-pathak/MatchZy](https://github.com/shobhit-pathak/MatchZy).

## Other CS2 project

For another CS2-related project, check out
[CliptionCode/CS2-Replay-Viewer](https://github.com/CliptionCode/CS2-Replay-Viewer).

## Donations

If you would like to support the development of MatchZy-Refined, you can donate
via PayPal.

<a href="https://paypal.me/cliption">
  <img src="https://img.shields.io/badge/Donate-PayPal-00457C?style=for-the-badge&logo=paypal&logoColor=white" alt="Donate with PayPal" />
</a>

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for
details.
