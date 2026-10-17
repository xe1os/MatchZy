using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice commands on the bot at your aim:
    //   .kevlar           - toggle the bot's kevlar (body armor, no helmet)
    //   .armor            - toggle the bot's full armor (kevlar + helmet)
    //   .botgun <weapon>  - give the bot a weapon (ak47, awp, deagle, ...), replacing its gun in that slot
    //   .dismiss          - remove the bot at your aim
    //   .dismissall       - remove every practice bot (same as .clearbots)
    //
    // The choice is kept per bot and applied again on every respawn (prac.cfg's mp_free_armor and the
    // team's default pistol would undo it otherwise).
    public partial class MatchZy
    {
        private readonly Dictionary<int, (int Armor, bool Helmet)> botArmorOverrides = new();
        private readonly Dictionary<int, string> botGunOverrides = new();

        private static readonly HashSet<string> BotPistols = new(StringComparer.Ordinal)
        {
            "weapon_glock", "weapon_hkp2000", "weapon_usp_silencer", "weapon_p250", "weapon_elite",
            "weapon_fiveseven", "weapon_tec9", "weapon_cz75a", "weapon_deagle", "weapon_revolver",
        };

        private static readonly HashSet<string> BotPrimaries = new(StringComparer.Ordinal)
        {
            "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_galilar", "weapon_famas",
            "weapon_sg556", "weapon_aug", "weapon_awp", "weapon_ssg08", "weapon_g3sg1", "weapon_scar20",
            "weapon_mac10", "weapon_mp9", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90",
            "weapon_bizon", "weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_mag7",
            "weapon_m249", "weapon_negev",
        };

        // Short names people type, mapped to the class name.
        private static readonly Dictionary<string, string> BotGunAliases = new(StringComparer.Ordinal)
        {
            { "m4", "weapon_m4a1" }, { "m4a4", "weapon_m4a1" }, { "m4a1s", "weapon_m4a1_silencer" },
            { "m4a1-s", "weapon_m4a1_silencer" }, { "usp", "weapon_usp_silencer" }, { "usps", "weapon_usp_silencer" },
            { "usp-s", "weapon_usp_silencer" }, { "p2000", "weapon_hkp2000" }, { "galil", "weapon_galilar" },
            { "sg553", "weapon_sg556" }, { "krieg", "weapon_sg556" }, { "scout", "weapon_ssg08" },
            { "auto", "weapon_scar20" }, { "cz", "weapon_cz75a" }, { "cz75", "weapon_cz75a" },
            { "five7", "weapon_fiveseven" }, { "duals", "weapon_elite" }, { "dualies", "weapon_elite" },
            { "r8", "weapon_revolver" }, { "deag", "weapon_deagle" }, { "mp5", "weapon_mp5sd" },
            { "ump", "weapon_ump45" }, { "pp-bizon", "weapon_bizon" }, { "xm", "weapon_xm1014" },
        };

        // The tracked practice bot closest to the crosshair, within a small cone (wider up close, so a
        // bot next to you is easy to pick). No line-of-sight check: in practice you aim at what you mean.
        private CCSPlayerController? GetBotAtAim(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || pawn.AbsOrigin == null)
                return null;
            var origin = pawn.AbsOrigin;
            QAngle eye = pawn.EyeAngles;
            double pitch = eye.X * Math.PI / 180.0;
            double yaw = eye.Y * Math.PI / 180.0;
            double fx = Math.Cos(pitch) * Math.Cos(yaw);
            double fy = Math.Cos(pitch) * Math.Sin(yaw);
            double fz = -Math.Sin(pitch);
            double ex = origin.X, ey = origin.Y, ez = origin.Z + 64.0;

            CCSPlayerController? best = null;
            double bestAngle = double.MaxValue;
            foreach (var bot in TrackedPracticeBots())
            {
                if (!IsPlayerValid(bot) || !bot.PawnIsAlive)
                    continue;
                var botOrigin = bot.PlayerPawn.Value?.AbsOrigin;
                if (botOrigin == null)
                    continue;
                double dx = botOrigin.X - ex, dy = botOrigin.Y - ey, dz = botOrigin.Z + 40.0 - ez;
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 1.0 || len > 8000.0)
                    continue;
                double cos = Math.Clamp((dx * fx + dy * fy + dz * fz) / len, -1.0, 1.0);
                double angle = Math.Acos(cos) * 180.0 / Math.PI;
                // Half a body width (~32u) seen from this distance, between 3 and 15 degrees.
                double allowed = Math.Clamp(Math.Atan(32.0 / len) * 180.0 / Math.PI, 3.0, 15.0);
                if (angle <= allowed && angle < bestAngle)
                {
                    best = bot;
                    bestAngle = angle;
                }
            }
            return best;
        }

        // Shared start of every at-aim command: practice on, a valid caller and a bot under the
        // crosshair. Replies when there is none.
        private CCSPlayerController? RequireBotAtAim(CCSPlayerController? player)
        {
            if (!RequirePractice(player) || player == null || !IsPlayerValid(player))
                return null;
            var bot = GetBotAtAim(player);
            if (bot == null || !bot.UserId.HasValue)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.noaimbot"));
                return null;
            }
            return bot;
        }

        [ConsoleCommand("css_kevlar", "Toggle kevlar of the practice bot at your aim")]
        public void OnKevlarConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotArmorCommand(player, helmet: false);

        [ConsoleCommand("css_armor", "Toggle full armor (kevlar + helmet) of the practice bot at your aim")]
        public void OnArmorConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotArmorCommand(player, helmet: true);

        // helmet false: .kevlar (vest only), true: .armor (vest + helmet). Either one on a bot that
        // already has that much armor takes all of it off.
        private void HandleBotArmorCommand(CCSPlayerController? player, bool helmet)
        {
            var bot = RequireBotAtAim(player);
            if (bot == null)
                return;
            var pawn = bot.PlayerPawn.Value!;
            bool hasHelmet = BotHasHelmet(pawn);
            bool hasIt = pawn.ArmorValue > 0 && (!helmet || hasHelmet);
            (int armor, bool withHelmet) = hasIt ? (0, false) : (100, helmet);
            botArmorOverrides[bot.UserId!.Value] = (armor, withHelmet);
            string botName = bot.PlayerName;
            // Off the command stack (rule: no engine re-entry inside a command handler).
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(bot) && bot.PawnIsAlive)
                    ApplyBotArmor(bot, armor, withHelmet);
            });
            string key = helmet
                ? (armor > 0 ? "matchzy.px.armoron" : "matchzy.px.armoroff")
                : (armor > 0 ? "matchzy.px.kevlaron" : "matchzy.px.kevlaroff");
            ReplyToUserCommand(player, Localizer.ForPlayer(player, key, botName));
        }

        private static bool BotHasHelmet(CCSPlayerPawn pawn)
        {
            var items = pawn.ItemServices;
            return items != null && items.Handle != IntPtr.Zero && new CCSPlayer_ItemServices(items.Handle).HasHelmet;
        }

        private static void ApplyBotArmor(CCSPlayerController bot, int armor, bool helmet)
        {
            var pawn = bot.PlayerPawn.Value;
            if (pawn == null)
                return;
            pawn.ArmorValue = armor;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            var items = pawn.ItemServices;
            if (items != null && items.Handle != IntPtr.Zero)
            {
                new CCSPlayer_ItemServices(items.Handle).HasHelmet = helmet;
                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
            }
        }

        [ConsoleCommand("css_botgun", "Give the practice bot at your aim a weapon: css_botgun <weapon>")]
        public void OnBotGunConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotGunCommand(player, command.ArgString);

        private void HandleBotGunCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || player == null || !IsPlayerValid(player))
                return;
            string? weapon = NormalizeBotGun(arg);
            if (weapon == null)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botguninvalid", (arg ?? "").Trim()));
                return;
            }
            var bot = RequireBotAtAim(player);
            if (bot == null)
                return;
            botGunOverrides[bot.UserId!.Value] = weapon;
            string botName = bot.PlayerName;
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(bot) && bot.PawnIsAlive)
                    GiveBotGun(bot, weapon);
            });
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botgunset", botName, weapon.Substring("weapon_".Length)));
        }

        // "ak47", "AK-47", "weapon_ak47" and the aliases above -> a known class name, else null.
        private static string? NormalizeBotGun(string? arg)
        {
            string name = (arg ?? "").Trim().Trim('"').ToLowerInvariant();
            if (name.Length == 0)
                return null;
            if (BotGunAliases.TryGetValue(name, out var alias))
                return alias;
            name = name.Replace("-", "");
            if (BotGunAliases.TryGetValue(name, out alias))
                return alias;
            if (!name.StartsWith("weapon_", StringComparison.Ordinal))
                name = "weapon_" + name;
            return BotPistols.Contains(name) || BotPrimaries.Contains(name) ? name : null;
        }

        // Replace the gun in the weapon's slot (a pistol replaces the pistol, anything else the
        // primary), then switch to it. The old one is removed with entity-IO Kill, never Remove().
        private void GiveBotGun(CCSPlayerController bot, string weapon)
        {
            var services = bot.PlayerPawn.Value?.WeaponServices;
            if (services == null)
                return;
            bool pistol = BotPistols.Contains(weapon);
            var slotSet = pistol ? BotPistols : BotPrimaries;
            var held = new List<string>();
            foreach (var h in services.MyWeapons)
            {
                var w = h.Value;
                if (w != null && w.IsValid && slotSet.Contains(w.DesignerName))
                    held.Add(w.DesignerName);
            }
            foreach (var name in held.Distinct())
                RemoveWeaponByName(bot, name);
            bot.GiveNamedItem(weapon);
            bot.ExecuteClientCommandFromServer(pistol ? "slot2" : "slot1");
        }

        // Respawn: put the bot's armor and gun back once the engine has handed out the defaults.
        private void ReapplyBotLoadout(CCSPlayerController bot)
        {
            if (!bot.UserId.HasValue)
                return;
            int uid = bot.UserId.Value;
            bool hasArmor = botArmorOverrides.TryGetValue(uid, out var armor);
            bool hasGun = botGunOverrides.TryGetValue(uid, out var gun);
            if (!hasArmor && !hasGun)
                return;
            AddTimer(0.15f, () =>
            {
                if (!isPractice || !IsPlayerValid(bot) || !bot.PawnIsAlive)
                    return;
                if (hasGun)
                    GiveBotGun(bot, gun!);
                if (hasArmor)
                    ApplyBotArmor(bot, armor.Armor, armor.Helmet);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void ForgetBotLoadout(int botUserId)
        {
            botArmorOverrides.Remove(botUserId);
            botGunOverrides.Remove(botUserId);
        }

        private void ResetBotLoadouts()
        {
            botArmorOverrides.Clear();
            botGunOverrides.Clear();
        }

        [ConsoleCommand("css_dismiss", "Remove the practice bot at your aim")]
        public void OnDismissConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            var bot = RequireBotAtAim(player);
            if (bot == null)
                return;
            KickPracticeBot(player, bot);
        }

        [ConsoleCommand("css_dismissall", "Remove all practice bots")]
        public void OnDismissAllConsoleCommand(CCSPlayerController? player, CommandInfo? command)
            => OnNoBotsCommand(player, command);
    }
}
