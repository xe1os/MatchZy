using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace MatchZy
{
    // .friendlyfire / .ffire: like .knife, an admin decides before the match (or scrim / hill) goes
    // live whether friendly fire is on. Applied after the live, scrim or hill cfg and the match
    // config's cvars on every map of the series; cleared when the match is reset.
    // Grenades-only mode (.ffire nades, or "friendly_fire": "grenades" in the match config) keeps
    // mp_friendlyfire 1 and zeroes team damage in OnPlayerTakeDamagePre unless it is blast (HE) or
    // burn (molotov / incendiary) damage. mp_friendlyfire 0 would remove grenade team damage too.
    public partial class MatchZy
    {
        // null = the mode cfg decides (mp_friendlyfire 1 in the shipped cfgs).
        private bool? friendlyFireOverride;
        private bool friendlyFireGrenadesOnly;

        private const DamageTypes_t FriendlyFireGrenadeDamage = DamageTypes_t.DMG_BLAST | DamageTypes_t.DMG_BURN;

        [ConsoleCommand("css_friendlyfire", "Toggles friendly fire for the match before it goes live")]
        [ConsoleCommand("css_ffire", "Toggles friendly fire for the match before it goes live")]
        public void OnFriendlyFireCommand(CCSPlayerController? player, CommandInfo? command)
        {
            HandleFriendlyFireCommand(player, command?.ArgCount > 1 ? command.ArgByIndex(1) : "");
        }

        // No argument toggles on/off; "on", "off" or "nades" / "grenades" set the mode.
        private void HandleFriendlyFireCommand(CCSPlayerController? player, string arg)
        {
            if (!IsPlayerAdmin(player, "css_friendlyfire", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (isPractice)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.friendlyfirepractice"));
                return;
            }
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.friendlyfirestarted"));
                return;
            }

            switch (arg.Trim().ToLowerInvariant())
            {
                case "":
                    // Toggling from grenades-only turns friendly fire off.
                    bool on = friendlyFireGrenadesOnly || (friendlyFireOverride ?? CfgFriendlyFireDefault());
                    SetFriendlyFireMode(on ? "off" : "on");
                    break;
                case "on": case "1": case "true":
                    SetFriendlyFireMode("on");
                    break;
                case "off": case "0": case "false":
                    SetFriendlyFireMode("off");
                    break;
                case "nades": case "grenades": case "nade":
                    SetFriendlyFireMode("grenades");
                    break;
                default:
                    ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.friendlyfireusage"));
                    return;
            }
            string status = Localizer.ForPlayer(player, friendlyFireGrenadesOnly ? "matchzy.cc.friendlyfiregrenades"
                : friendlyFireOverride == true ? "matchzy.cc.enabled" : "matchzy.cc.disabled");
            PrintLocalizedToAll("matchzy.cc.friendlyfire", status);
        }

        // "on", "off" or "grenades". Also used for the match config's "friendly_fire" field.
        private void SetFriendlyFireMode(string mode)
        {
            friendlyFireGrenadesOnly = mode == "grenades";
            friendlyFireOverride = mode != "off";
        }

        private HookResult OnPlayerTakeDamagePreFriendlyFire(CCSPlayerPawn victim, CTakeDamageInfo info)
        {
            try
            {
                if (!friendlyFireGrenadesOnly || !matchStarted || isPractice)
                    return HookResult.Continue;
                var attacker = info.Attacker.Value;
                // World damage (fall, trigger_hurt) and self damage are not friendly fire.
                if (attacker == null || !attacker.IsValid || attacker.DesignerName != "player" || attacker.Handle == victim.Handle)
                    return HookResult.Continue;
                if (attacker.TeamNum != victim.TeamNum)
                    return HookResult.Continue;
                if ((info.BitsDamageType & FriendlyFireGrenadeDamage) != 0)
                    return HookResult.Continue;
                info.Damage = 0;
            }
            catch (Exception e)
            {
                Log($"[FriendlyFire FATAL] {e.Message}");
            }
            return HookResult.Continue;
        }

        // What the mode cfg the match will use sets, so the first toggle flips the real value.
        private bool CfgFriendlyFireDefault()
        {
            try
            {
                string cfg = isPlayOutEnabled2 ? hillCfgPath
                    : isPlayOutEnabled ? scrimCfgPath
                    : LiveCfgPathForMode();
                string? value = GetConvarValueFromCFGFile(Path.Join(Server.GameDirectory + "/csgo/cfg", cfg), "mp_friendlyfire");
                return value == null || (value.Trim().Trim('"') != "0" && !value.Trim().Trim('"').Equals("false", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        // Called after the live / scrim / hill cfg has been executed.
        private void ApplyFriendlyFireOverride()
        {
            if (friendlyFireOverride is bool enabled)
                Server.ExecuteCommand($"mp_friendlyfire {(enabled ? 1 : 0)}");
        }
    }
}
