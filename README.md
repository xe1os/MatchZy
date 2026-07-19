# MatchZy-Refined

MatchZy-Refined is a fork of
[shobhit-pathak/MatchZy](https://github.com/shobhit-pathak/MatchZy) that refines
the original project.

This fork includes all functionality available in the forked repository and
adds the following focused features, fixes, and changes.

## Features

- Added `.menu` for a per-player center-screen configuration menu covering practice startup, bot placement and removal, bot settings, player settings and saved positions, spawn controls, and utility actions. Use `W`/`S` to navigate and `A`/`D` to change values or select actions.
- Added `.grt` as an explicit practice-mode command for CS2's server-global grenade rethrow.
- Added `.slp` to save the player's current position and view direction, `.tlp` to return to it, and `.dlp` to delete it.
- Added interactive spawn outlines in practice mode; stand inside a flat gold T-side or blue CT-side square and press `E` to teleport to that exact spawn. Each outline and its interaction zone use the cached local ground height at the spawn, `.spawnmarkers` toggles visibility, and `matchzy_spawn_markers_enabled_default` controls whether markers appear when practice starts.
- Added `.sbp <name>`, `.lbp <name>`, `.dbp <name>`, and `.listbp` for saving, restoring, deleting, and listing named, multi-word bot-position setups with their teams, aim directions, and crouch states. Saved setups can also be listed, loaded, and deleted from Bot Placement in `.menu`.
- Added `.botspawn <name>`, `.delbotspawn <name>`, `.listbotspawn`, and `.placebot <number> <name>` for building map-specific named spawn pools and placing utility-free bots at randomly selected, distinct saved XY positions and aim directions.
- Added `.kicklastbot` to remove the most recently placed practice bot; repeated uses work backward through the placement order.
- Added `.changemap <map-name>` so any player can switch to a validated map, with or without the `de_` prefix.
- Added `.botshoot` and `.botreactiontime <0-1000>` to turn practice bots into stationary, visibility-aware opponents and configure their firing delay.
- Added `.botrespawn` and `.botlifereg` to control practice-bot respawning and health regeneration independently of human players; respawned tracked bots return to their last placed position and aim direction.
- Added per-player `.liferegon` health regeneration for human players in practice mode, enabled by default and individually opt-out.
- Added public `.allliferegon <true/false>` control so any player can enable or disable human health regeneration for everyone in the current practice session, including later joiners.
- Added `.ammo` so any player can toggle infinite ammunition for the entire practice session; Start Round is also available directly below Start Practice in `.menu`.

## BugFixes

- Disconnecting during practice no longer leaves behind a respawned, attackable player pawn or persistent corpse; dropped C4 and equipment remain in the world.
- `.rt`, `.rethrow`, and `.throw` now use the requesting player's own utility history during multiplayer practice sessions.
- Smoke and HE grenade rethrows now recreate the correct projectile, follow the recorded trajectory, and detonate normally.
- Player teleports no longer apply camera pitch or roll to the player model.
- Nade deletion commands now resolve multi-word lineup queries consistently with `.loadnade`.
- Spawn commands and markers now use each map's primary competitive T and CT spawn groups, with higher-priority fallbacks used only when needed to provide five spawns per side.

## Changes

- Practice mode allows unrestricted teammate damage without warnings, punishment, or automatic kicks.
- Human and bot practice death respawns now use a 500 ms delay; humans use the same delay after joining a team.
- Practice side switching replaces a carried T-side Molotov with a CT incendiary grenade and removes the defuse kit when moving from CT to T.
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
- `.help` keeps its private chat list compact by showing command names without argument hints and prints detailed usage to the requesting player's console.
- `.botshoot`, `.botrespawn`, `.botlifereg`, and `.liferegon` now toggle their current state when used without an argument; explicit `true` or `false` values remain supported.
- Plugin-managed practice bots are created directly on their intended enemy side. Removal releases their team membership before disconnecting the fake clients, clearing dead roster slots without runtime quota rewrites; full-team `.bot` requests return promptly without disturbing the existing setup.
- Practice bots controlled by `.botshoot` stop firing when an active smoke blocks their sightline and reacquire their configured reaction delay after visibility returns.
- `.startround` starts a fresh practice round with a five-second freeze, assigns each human a different random team spawn, and keeps every tracked bot alive at its placed position and orientation.
- `.randomspawn` safely teleports the requesting player to a randomly selected competitive spawn for their current T or CT side.
- Temporary player/bot collision handling now stops safely when either pawn disappears or is replaced, preventing repeating null-reference errors during respawns, round restarts, disconnects, and bot removal.
- Every `.placebot` and `.lbp` load kicks all existing bots, waits at least 500 ms for cleanup, and then creates the requested setup from scratch.
- `.watchme` / `.fas` is ignored unless the requesting player is alive and currently playing on the T or CT side.

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
