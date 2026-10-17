# Practice mode

`.prac` turns the server into a practice server: unlimited money, buy anywhere, endless rounds, no team-damage kicks, and bullet impacts on. Every command below is listed in full in [Commands > Practice mode](../reference/commands.md#practice-mode).

## Learn a lineup

1. Throw the grenade.
2. `.last` puts you back where you threw it, grenade in hand. Keep typing `.back` to step through older throws.
3. `.rt` rethrows it from the same spot without you moving, so you can watch it land.
4. Happy with it? `.savenade <name> jump Smoke for CT` stores it (the throw style is optional).
5. Later: `.ln <name>` or `.ln #3` loads it again.

Helpers while learning:

- `.arc` draws the flight path, `.landmarker` marks the detonation point.
- `.autoclear` keeps only the latest utility on the map; `.cleanup` removes it all.
- `.ff` fast-forwards 10 seconds; `.timer` is a stopwatch.
- `.flashtest` shows your blind time; `.blind` pops a flash in your face for reaction reps.
- Detonation times, including molotov burn time, are printed in chat.

## Grenade library

`.shownades` draws a colored marker at every saved lineup on the map (smoke blue, flash yellow, HE red, molotov orange, decoy grey) with a label showing type, comment and throw style. A small glowing grenade marks each spot through walls, so you can find lineups from across the map. `.shownades smoke` (or `flash`, `he`, `molly`, `mine`, or part of a name) shows only those.

- Aim at a marker and press ++e++ to teleport there with the right grenade.
- Several lineups on one spot share a marker; press ++f++ to cycle them.
- The marker you stand on hides for you, so it never blocks your throw.
- `.nades` opens a menu to browse lineups by type.
- Admins add lineups to a shared server-wide pack with `.libadd <name>` (`.libremove`, `.liblist`).

## Team executes

- `.delay <seconds>` sets the timing of your last grenade.
- `.grt` rethrows **everyone's** last grenade at once, each with its delay, so a whole execute plays out in one command.
- `.dryrun` then plays the execute in real rounds.

## Bots

- `.bot` places a bot where you stand, looking where you look. `.crouchbot`, `.boost`, `.crouchboost` for other setups.
- `.savebotpos <name>` saves a bot spot; `.loadbotpos` with no name spawns every saved spot on the map, rebuilding a full setup instantly.
- `.showbotpos` shows the saved spots; `.botjiggle` makes bots strafe for peek practice (`.botjigglerange`, `.botjigglerandom`).
- `.botspawn <name>` builds a pool of spots; `.placebot <number> <name>` puts bots on random spots from it, so every rep has a new setup. `.showbotspawn` shows the spots (stand in one + ++e++ deletes it).
- `.botshoot` makes bots fight back from their spot after `.botreactiontime` (default 500 ms). Turn off your health regeneration with `.liferegon` (or `.allliferegon false` for everyone) so their hits count. `.botrespawn` and `.botlifereg` control what happens when you hit them.
- Aim at a bot: `.kevlar` / `.armor` take its armor off or put it back, `.botgun awp` changes its weapon, `.dismiss` removes it, `.runboost` lets you ride it while it copies your movement. `.xray` shows everyone through walls.
- `.stratrec` records your whole team's execute at once (3 second countdown, everyone starts together); `.strat` plays it back with one bot per role, `.strat <name> me 2` lets you play role 2 against the timing of the others. `.savestrat` keeps it per map. Alone? `.createstrat <name>` and then `.stratslot <name> 1`, `2`, ... records one role at a time while bots play the roles you already recorded; `.strats` does the same from a menu.
- `.startround` resets the rep: utility cleared, random spawns for every player, bots back on their spots, 5 second freeze.
- Bot commands respect `-nobots` and never take the CSTV slot, so a practice session cannot kill a running GOTV.

## Positions and spawns

- `.savepos <name>` / `.loadpos <name>`: up to 32 named positions. `.slp` / `.tlp` / `.dlp` work on the unnamed slot, and after a death you respawn there.
- `.showspawns`: a numbered box on the floor at every competitive spawn (blue CT, orange T); aim at a box (it turns green) or stand inside one and press ++e++ to teleport there. The number is the one `.spawn <n>` uses.
- `.spawn <n>`, `.bestspawn`, `.worstspawn` to practice spawn-dependent timings.

## Team switching

`.t`, `.ct` and `.spec` switch sides without counting a death. Switching from spectator uses the game's own join flow, so you spawn right away.
