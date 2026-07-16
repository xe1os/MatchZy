# MatchZy-Refined

MatchZy-Refined is a fork of
[shobhit-pathak/MatchZy](https://github.com/shobhit-pathak/MatchZy) that refines
the original project.

This fork includes all functionality available in the forked repository and
adds the following focused features, fixes, and changes.

## Features

- Added `.grt` as an explicit practice-mode command for CS2's server-global grenade rethrow.
- Added `.slp` to save the player's current position and view direction, and `.tlp` to return that player to their last saved location.
- Added interactive T and CT spawn beams in practice mode; stand at a green beam and press `E` to teleport to that exact spawn.
- Added `.sbp <name>`, `.lbp <name>`, and `.dbp <name>` for saving, restoring, and deleting named, multi-word bot-position setups with their teams, aim directions, and crouch states.

## BugFixes

- `.rt`, `.rethrow`, and `.throw` now use the requesting player's own utility history during multiplayer practice sessions.
- Smoke and HE grenade rethrows now recreate the correct projectile, follow the recorded trajectory, and detonate normally.
- Player teleports no longer apply camera pitch or roll to the player model.
- Nade deletion commands now resolve multi-word lineup queries consistently with `.loadnade`.
- Spawn commands and markers now use each map's primary competitive T and CT spawn groups, with higher-priority fallbacks used only when needed to provide five spawns per side.

## Changes

- Rethrow commands use per-player cooldowns and clear the corresponding state when a player disconnects.

## Upstream project

The original project and its history are available at
[shobhit-pathak/MatchZy](https://github.com/shobhit-pathak/MatchZy).

## Donations

If you would like to support the development of MatchZy-Refined, you can donate
via PayPal.

<a href="https://paypal.me/cliption">
  <img src="https://img.shields.io/badge/Donate-PayPal-00457C?style=for-the-badge&logo=paypal&logoColor=white" alt="Donate with PayPal" />
</a>

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for
details.
