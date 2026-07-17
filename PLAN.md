# Plan: MatchZy-Refined Center-HTML Configuration Menu

## Objective

Add an in-game, per-player configuration menu that opens and closes with `.menu` and is displayed through CS2's center-HTML HUD surface.

The root title must be exactly:

**MATCHZY-REFINED CONFIGURATION**

The initial root sections are:

1. **Bot Configuration**
2. **Player Configuration**
3. **Spawns**
4. **Utility**
5. **Close Menu**

The menu should behave like a cursor-based menu instead of CounterStrikeSharp's normal numbered menu:

- Forward / W: move the selection up.
- Back / S: move the selection down.
- Move left / A: choose the previous value, set a boolean off, or perform the contextual left action.
- Move right / D: choose the next value, set a boolean on, enter a section, or activate an action.
- Entering `.menu` while the menu is already open closes it.
- Submenus contain an explicit **Back** row because A is also needed for changing values.

Up, down, left, and right arrow keys must be treated as aliases only when the player's CS2 bindings make them emit the same movement actions. A server plugin receives semantic player-button actions, not raw physical keyboard scan codes. Do not rebind a player's keys from the server.

## Important CounterStrikeSharp Constraint

CounterStrikeSharp's built-in `CenterHtmlMenu` supplies the center-screen presentation, option callbacks, numbered selection, and paging, but it does not natively provide:

- A selected or hovered row.
- W/S cursor movement.
- A/D value changes.
- Arrow-key detection.
- A way to cancel the movement caused by W/A/S/D.

Its normal presentation is based on numbered entries such as `!1`, with selection routed through `MenuManager.OnKeyPress(int)`.

Therefore, do not assume that constructing a normal `CenterHtmlMenu` is sufficient. Implement a small internal cursor-based menu controller that uses the same center-HTML HUD surface, most likely by rendering with `PrintToCenterHtml`. Reusing `CenterHtmlMenu` as a data model is acceptable if useful, but its numbered input and default renderer should not control this feature.

Do not add a third-party menu dependency unless the internal approach proves unworkable and the dependency is reviewed for API 371 compatibility, lifecycle cleanup, and maintenance risk.

## Required Feasibility Checks Before Full Implementation

Complete these checks on a CS2 test server before building all menu screens:

1. Confirm that `Listeners.OnPlayerButtonsChanged` reports the pressed-edge transitions for Forward, Back, Moveleft/Left, and Moveright/Right for a human player.
2. Confirm that a quick press produces exactly one navigation action and determine what happens when a key is held.
3. Confirm which default CS2 bindings, if any, make the arrow keys emit those actions. W/A/S/D support is required; arrow support is best effort and binding-dependent.
4. Render a five-row center-HTML prototype with a visibly highlighted row and verify it at common resolutions and aspect ratios.
5. Determine how frequently the center-HTML content must be refreshed to remain visible without flicker.
6. Determine whether player movement can be safely suppressed while the menu is open. `OnPlayerButtonsChanged` is a notification and does not itself cancel movement.
7. Verify that clearing the center message removes the menu immediately and does not interfere with unrelated center messages longer than necessary.

The implementation must not proceed on the assumption that physical arrow keys or movement suppression are automatically available.

## Proposed User Experience

### Root screen

Display the exact title followed by the five root rows. The selected row should be visually distinct using both color and a prefix such as `>` so selection remains understandable for users who cannot distinguish the colors.

Selecting **Bot Configuration**, **Player Configuration**, **Spawns**, or **Utility** with D/right enters that section. Selecting **Close Menu** closes the menu and clears the center display.

The footer should briefly explain the controls:

**W/S Navigate | A/D Change or Select | .menu Close**

Do not show numbered `!1` hints because this menu is not controlled through numbered chat commands.

### Bot Configuration screen

Make it visually clear that these values affect the shared practice session, not only the requesting player.

Required rows:

1. **Back**
2. **Bot Shooting** — live value `ON` or `OFF`; maps to `.botshoot`.
3. **Bot Reaction Time** — live value in milliseconds; maps to `.botreactiontime`.
4. **Bot Respawn** — live value `ON` or `OFF`; maps to `.botrespawn`.
5. **Bot HP Regeneration** — live value `ON` or `OFF`; maps to `.botlifereg`.

Boolean rows use A/left for `OFF` and D/right for `ON`. Re-selecting the already active value should be harmless and should not produce misleading state changes.

Bot reaction time must remain within the existing accepted range of 0 through 1000 milliseconds. Use 50-millisecond A/D steps, clamp at both limits, and start from the current live value. This preserves the existing default of 500 milliseconds while allowing the entire command range. If later feedback requires finer control, the step can be made configurable without changing the menu architecture.

The initial version intentionally excludes bot-creation commands and named bot-position presets. `.bot`, `.cbot`, `.boost`, `.crouchboost`, and `.nobots` are actions rather than configuration values. `.sbp`, `.lbp`, and `.dbp` require a name or preset-selection workflow. They can later be added under a separate **Bot Actions** or **Bot Presets** screen without overloading this configuration screen.

### Player Configuration screen

Make it visually clear that these values and actions apply only to the requesting player.

Required rows:

1. **Back**
2. **Regenerate HP** — live value `ON` or `OFF`; maps to `.liferegon`.
3. **God Mode** — live value `ON` or `OFF`; maps to `.god`.
4. **Flash Protection** — live value `ON` or `OFF`; maps to `.noflash` / `.noblind`.
5. **Store Last Position** — action; maps to `.slp`.
6. **Teleport to Last Position** — action; maps to `.tlp`.
7. **Delete Last Position** — action; maps to `.dlp`.

Use the descriptive labels above rather than displaying command names.

For the position actions, show whether a stored position currently exists. **Teleport to Last Position** and **Delete Last Position** should render as unavailable when no position is stored, and attempting to activate them must preserve the existing user feedback rather than silently doing nothing.

Keep the menu open after performing a player action, refresh its live state immediately, and show a short success or error status line. Closing after teleporting may be considered later if leaving the menu open proves confusing in playtesting.

### Spawns screen

Required rows:

1. **Back**
2. **Show Spawn Locations** — action; maps to `.showspawns`.
3. **Hide Spawn Locations** — action; maps to `.hidespawns`.
4. **Change to T-Side** — action; maps to `.t`.
5. **Change to CT-Side** — action; maps to `.ct`.

Use the descriptive labels above rather than displaying command names. D/right activates the selected action; A/left does not activate it.

**Show Spawn Locations** and **Hide Spawn Locations** affect the shared practice-session spawn markers. Their menu actions must preserve the current behavior, including loading or displaying the competitive T and CT spawn outlines, removing them cleanly, and retaining the existing instruction that a player can stand inside an outline and press E to teleport.

The menu should communicate the current marker visibility when that state can be derived reliably. Prefer showing the relevant action as unavailable when it would make no change—for example, disable **Show Spawn Locations** while markers are already visible and disable **Hide Spawn Locations** while they are hidden. Do not create a second, menu-only visibility flag; derive the state from the authoritative marker lifecycle.

**Change to T-Side** and **Change to CT-Side** apply only to the requesting player. Preserve the existing practice-mode team-switch behavior, including its current spectator restriction and feedback. Display the requesting player's current side in the screen subtitle or beside the actions, and make the action for their current side unavailable. Because switching teams can replace or invalidate the player's pawn, refresh or close the menu only after revalidating the controller and current pawn. The safer initial behavior is to close the menu after a successful side change and let the player reopen it with `.menu`.

### Utility screen

Required rows:

1. **Back**
2. **Rethrow Utility** — action; maps to `.rt`.
3. **Clear all Utilities** — action; maps to `.clear`.
4. **Rethrow Smoke** — action; maps to `.rethrowsmoke`.
5. **Rethrow Flashbang** — action; maps to `.rethrowflash`.
6. **Rethrow Molotov** — action; maps to `.rethrowmolotov`.
7. **Rethrow Decoy** — action; maps to `.rethrowdecoy`.
8. **Rethrow last Util** — action; maps to `.grt`.

Use the descriptive labels above rather than displaying command names. This screen exceeds the five-row rendering target, so its implementation must exercise the planned scrolling behavior and keep the selected row visible.

The difference between the two general rethrow actions must be stated clearly in contextual help or a short status line:

- **Rethrow Utility** (`.rt`) uses the requesting player's own recorded utility history and preserves that utility's configured delay.
- **Rethrow last Util** (`.grt`) invokes CS2's server-global last-grenade rethrow and is not scoped to the requesting player's history.

All utility rows are immediate actions activated with D/right. Keep the menu open after an action, revalidate the player before rendering again, and show the existing success or error feedback in an appropriate form. Preserve the existing per-player cooldown for `.rt` and the existing per-requesting-player cooldown for `.grt`; menu input must not bypass them.

For the type-specific rethrow rows, derive availability from the requesting player's recorded grenade data. Render a type as unavailable when that player has not thrown the corresponding smoke, flashbang, molotov/incendiary, or decoy. **Rethrow Utility** should likewise be unavailable without a personal grenade history. Do not infer availability for **Rethrow last Util** from personal history because it uses server-global CS2 state; if that global state cannot be queried reliably, leave the row activatable and preserve the engine's normal no-history behavior.

**Clear all Utilities** must retain the current `.clear` semantics rather than implying a broader reset: remove active utility effects and dropped practice equipment while preserving the C4/bomb and player-held equipment. Its label is the requested user-facing wording; add concise contextual help if needed to explain the exact scope.

## Menu State Model

Maintain one independent menu session per connected human player. A session needs to remember:

- Whether the menu is open.
- The current screen: root, bot configuration, player configuration, spawns, or utility.
- The selected row on that screen.
- The visible scroll window when a screen has more rows than can safely be rendered.
- The last accepted input time for debouncing.
- Optional short-lived feedback text and its expiry time.
- Any movement state captured for safe restoration if movement suppression is implemented.

Use the same connection-lifetime identifier convention as the surrounding plugin, but clean state aggressively so reused slots or user IDs cannot inherit another player's menu. Exclude bots, HLTV, invalid controllers, and server-console invocations.

Do not cache configuration values inside the menu session. Read the live values from MatchZy's actual state whenever the menu renders. This ensures that a command entered in chat by another player is immediately reflected in every open menu.

## Rendering Model

Create a reusable menu description containing rows with the following conceptual types:

- Section link.
- Boolean value.
- Bounded numeric value.
- Immediate action.
- Back.
- Close.

Render no more than five selectable rows at once, matching the practical size used by CounterStrikeSharp's center menu. If a screen contains more rows, scroll the visible window automatically so the selected row remains visible and display a small position or scroll indicator.

Recommended visual rules:

- Title: prominent and consistent on every screen.
- Section subtitle: identifies Bot Configuration, Player Configuration, Spawns, or Utility.
- Selected row: bright highlight plus `>` prefix.
- Normal row: neutral readable color.
- Disabled action: grey with an explanatory value such as `NOT STORED`.
- Boolean enabled: green `ON`.
- Boolean disabled: red or grey `OFF`.
- Footer: subdued control instructions.
- Feedback: a separate short line that does not change row indexes.

Escape all dynamic text before sending it to the center-HTML renderer. Player names or future preset names must never be allowed to inject formatting tags.

Use one shared refresh mechanism for all active menus rather than registering one permanent tick handler per player. Only render while at least one menu is open, and stop refreshing when no sessions remain. Choose the least frequent refresh interval that remains visually stable according to the feasibility test.

## Input Handling

The repository already registers `Listeners.OnPlayerButtonsChanged` and uses `OnPlayerButtonsChanged` for the spawn-marker Use interaction. Integrate menu dispatch without breaking that feature.

The current spawn-marker handler exits early when practice mode or spawn markers are unavailable. Restructure the dispatch order so menu input is considered independently before the spawn-marker checks. Do not make `.menu` depend on spawn markers being visible.

Input behavior:

1. Ignore released buttons; navigate from pressed-edge information.
2. Normalize Forward and equivalent forward bits to one Up action.
3. Normalize Back to one Down action.
4. Normalize Left/Moveleft to one Left action.
5. Normalize Right/Moveright to one Right action.
6. Handle at most one logical action per callback. Define a deterministic priority for simultaneous inputs.
7. Apply a small debounce to prevent key repeat or network transitions from skipping multiple rows.
8. Wrap selection from the first row to the last and from the last row to the first.
9. Ignore menu navigation while the player is invalid, dead if the chosen movement policy requires a live pawn, or no longer in practice mode.

Contextual left/right behavior:

- Root section: right enters; left has no effect.
- Boolean row: left sets off; right sets on.
- Numeric row: left decrements; right increments.
- Action row: right activates; left has no effect.
- Back row: right returns to the parent screen.
- Close row: right closes the menu.

Because the menu uses movement actions, evaluate the movement experience explicitly:

- Preferred behavior is to prevent the player from moving while a menu is open, but only if this can be done without corrupting noclip, ladder, spectator, death, respawn, or movement state.
- If safe suppression requires temporarily freezing the pawn, capture and restore the exact relevant state on every close and cleanup path. Verify that frozen players still send button transitions.
- If suppression is not safe with API 371, leave movement unchanged for the first version and document that navigation keys also move the player. Do not implement fragile memory hooks merely to suppress movement.

## Command Registration

Register `css_menu` so console-style invocation is available and ensure `.menu` is included in the repository's custom chat-command dispatch table.

The handler must behave as a toggle:

- Closed plus `.menu`: validate the player and practice state, then open at the root screen.
- Open plus `.menu`: close and clear the display.
- Server console, bot, HLTV, or invalid player: do nothing safely.
- Outside practice mode: do not open; return a clear message that the configuration menu is available only in practice mode.

Avoid prefix matching that could accidentally treat unrelated text such as `.menutest` as `.menu`.

## Reuse Existing Configuration Logic

Do not duplicate the behavior of the existing command handlers, and do not have menu rows concatenate and execute arbitrary command strings.

Refactor each configurable operation into a typed internal action used by both the command handler and the menu:

- Set bot shooting to a supplied boolean.
- Set bot reaction time to a validated integer.
- Set bot respawn to a supplied boolean.
- Set bot HP regeneration to a supplied boolean.
- Set player HP regeneration to a supplied boolean.
- Set player God Mode to a supplied boolean.
- Set player flash protection to a supplied boolean.
- Store, teleport to, and delete the player's saved position.
- Show and hide the shared practice spawn locations.
- Change the requesting player to T-Side or CT-Side.
- Rethrow the requesting player's last utility.
- Clear active utility effects and eligible dropped practice equipment.
- Rethrow the requesting player's last smoke, flashbang, molotov/incendiary, or decoy.
- Invoke the server-global last-utility rethrow.

The shared actions must preserve all current side effects, including bot AI resets, respawning dead practice bots, regeneration timers, healing behavior, God Mode health restoration, flash cleanup, chat/localized feedback, and saved-position validation.

Command handlers remain supported and should become thin parsing/adaptation layers around the shared actions. The menu should pass typed values directly and must not bypass practice-mode checks or player validation.

## Live-State Sources

Derive displayed values from the existing authoritative state:

- Bot Shooting: `botShootingEnabled`.
- Bot Reaction Time: `botReactionTimeMs`.
- Bot Respawn: `botRespawnEnabled`.
- Bot HP Regeneration: `botLifeRegenerationEnabled`.
- Player HP Regeneration: membership in `humanLifeRegenerationEnabled`.
- God Mode: membership in `humanGodModeEnabled`.
- Flash Protection: membership in `noFlashList`.
- Stored position availability: membership in `savedPlayerLocationData`.
- Spawn-marker visibility: derive from the authoritative `spawnMarkerBeams` lifecycle rather than a menu-only copy.
- Current player side: the requesting controller's live team value.
- Personal last-utility availability: the requesting player's entry in `lastGrenadesData`.
- Type-specific utility availability: the requesting player's entries in `nadeSpecificLastGrenadeData`.

Bot settings and spawn-marker visibility are global to the current practice session. Player settings, position data, current side, and personal grenade histories are per player. The `.grt` action is server-global. The UI must not imply otherwise.

## Authorization and Behavioral Compatibility

Preserve the authorization behavior of the existing commands. At present, the relevant practice settings validate the player and practice mode but do not require the player to be a MatchZy admin. Do not silently make the menu more or less privileged than the commands.

If server owners later request restricted global bot settings, add that as an explicit policy shared by both the commands and menu rather than applying it only to the menu.

## Lifecycle and Cleanup

Close the menu, clear its center text, and remove its state on all applicable lifecycle events:

- Second `.menu` invocation.
- Explicit **Close Menu** selection.
- Player disconnect.
- Map end or map change.
- Leaving practice mode or starting match mode.
- Plugin unload or hot reload.
- Invalid controller/session detection.
- Any failure while rendering or applying an action.

Decide during testing whether death, team change, or spectator transition should close the menu. Closing on these transitions is the safer initial behavior, especially if movement suppression stores pawn state.

Never restore captured movement state onto a different or newly reused pawn without verifying its handle and validity.

## Localization

The requested title and English labels must appear exactly as specified. Add dedicated localization keys rather than scattering user-facing strings through the controller.

At minimum, add complete English entries and verify CounterStrikeSharp's fallback behavior for the existing language files. Do not add unreviewed machine translations. Missing translations should fall back to English cleanly until maintainers provide translations.

Dynamic values such as `ON`, `OFF`, milliseconds, `STORED`, and `NOT STORED` should also use localizable templates.

## Suggested File Organization

Keep the menu feature isolated from the already large `PracticeMode.cs` file.

Likely changes:

- Add a new partial-class file dedicated to configuration-menu state, screen definitions, rendering, input dispatch, and cleanup.
- Update `MatchZy.cs` for `.menu` registration, listener dispatch integration, and lifecycle cleanup registration where appropriate.
- Refactor relevant portions of `PracticeMode.cs` into shared typed actions without changing their behavior.
- Add localization keys to `lang/en.json` and verify fallback behavior in the other language files.
- Update `README.md` only after the feature exists and its controls have been validated.

Avoid placing all menu logic directly in `Load` or in the existing chat-event callback.

## Implementation Sequence

1. Build the minimal feasibility prototype described above, containing only a root screen and cursor movement.
2. Resolve rendering persistence, debouncing, arrow-binding behavior, and movement suppression policy.
3. Add the per-player session model and robust open/close cleanup.
4. Register the exact `.menu` toggle command in both supported command paths.
5. Refactor existing command behavior into shared typed actions and verify command regression behavior before connecting the menu.
6. Implement the Bot Configuration screen and its live values.
7. Implement the Player Configuration screen and saved-position availability states.
8. Implement the Spawns screen, authoritative marker visibility, and safe post-team-switch behavior.
9. Implement the Utility screen, personal/global rethrow distinction, availability states, and existing cooldown behavior.
10. Add feedback messages, scrolling, disabled-row handling, and localization.
11. Add lifecycle cleanup and multi-player synchronization behavior.
12. Build, run automated checks available in the repository, and perform the manual server test matrix.
13. Document the feature only after the controls and limitations are confirmed in a real client.

## Verification Matrix

### Build and regression

- The project builds against the existing minimum CounterStrikeSharp API version 371.
- Existing practice commands retain their exact accepted ranges, side effects, and feedback.
- Spawn-marker E/Use teleport still works when the configuration menu is closed.
- Existing chat commands are not swallowed or duplicated by the new menu handler.

### Opening and closing

- `.menu` opens only for a valid human player in practice mode.
- A second `.menu` invocation closes it.
- **Close Menu** closes it.
- Center text is cleared on every close path.
- Opening the menu twice never creates duplicate refresh tasks or sessions.

### Navigation

- W and S move exactly one row per press.
- A and D perform the documented contextual behavior.
- Selection wraps correctly.
- The selected row remains in the five-row viewport.
- Held keys do not cause uncontrolled skipping.
- Arrow keys work when bound to the corresponding CS2 movement actions and fail harmlessly otherwise.
- Two players can navigate different screens and selections independently.

### Bot Configuration

- The four displayed values always match the authoritative practice state.
- Boolean changes update immediately and retain all command side effects.
- Reaction time changes in 50-millisecond steps and never leaves 0 through 1000.
- A change by one player is visible in another player's already open menu.
- The screen clearly identifies the settings as practice-session-wide.

### Player Configuration

- Each player's regeneration, God Mode, flash protection, and position state is independent.
- Store, teleport, and delete use the same validation and effects as `.slp`, `.tlp`, and `.dlp`.
- Teleport and delete are visibly unavailable without a stored position.
- Position availability refreshes immediately after store or delete.
- One player's menu never exposes or changes another player's saved position.

### Spawns

- **Show Spawn Locations** and **Hide Spawn Locations** always match the authoritative shared marker state.
- Showing markers retains the current competitive T/CT counts, rendering, and E-to-teleport interaction.
- Hiding markers removes all marker entities and refreshes every open Spawns screen.
- A marker-visibility change by one player is reflected in another player's open menu.
- **Change to T-Side** and **Change to CT-Side** affect only the requesting player.
- Selecting the player's existing side is unavailable or harmless.
- A successful team change does not leave a stale menu session, renderer, or movement-suppression state attached to the old pawn.
- The existing spectator-to-team restriction and its user feedback remain intact.

### Utility

- **Rethrow Utility** uses only the requesting player's grenade history and configured delay.
- Each type-specific rethrow uses only the requesting player's latest grenade of that type.
- Personal and type-specific actions are visibly unavailable when the corresponding history does not exist.
- **Rethrow last Util** remains explicitly server-global and does not accidentally use personal history.
- Existing `.rt` and `.grt` cooldown behavior also applies through the menu.
- **Clear all Utilities** preserves the C4 and player-held equipment while removing the same entities as `.clear`.
- The eight-row Utility screen scrolls correctly, keeps selection visible, and returns to the root without losing input control.
- One player's personal history never enables or rethrows another player's personal utility.

### Lifecycle

- Disconnecting removes the session.
- Map changes and practice-mode exit close all sessions.
- Hot reload leaves no stale refresh handlers or frozen players.
- Death, respawn, team changes, noclip, ladders, and spectator transitions do not leave movement corrupted.
- Exceptions in an action or render path close the affected menu safely and log enough context for diagnosis.

## Acceptance Criteria

The feature is complete when:

- `.menu` reliably toggles a menu titled **MATCHZY-REFINED CONFIGURATION**.
- The root menu contains **Bot Configuration** and **Player Configuration**.
- The root menu also contains **Spawns** and **Utility**.
- W/S navigation and A/D contextual interaction work for stock movement bindings.
- Arrow behavior is accurately documented as binding-dependent.
- All four current bot configuration values are visible and editable.
- The specified player settings and position actions use descriptive labels and work per player.
- The four requested spawn actions use descriptive labels and preserve shared-marker versus per-player team scope.
- The seven requested utility actions use descriptive labels and preserve personal versus server-global rethrow semantics.
- Menu values always reflect live MatchZy state rather than stale copies.
- Existing commands remain fully functional.
- Multi-player use and every cleanup path are verified on a real CS2 server.
- No client addon, Workshop content, or external UI dependency is required.
