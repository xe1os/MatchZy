using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice health regeneration.
    //   .liferegon [true|false]     - your own regeneration (on by default).
    //   .allliferegon <true|false>  - regeneration for every player, also players who join later.
    //   .botlifereg [true|false]    - regeneration for practice bots (off by default).
    //
    // Default practice (prac.cfg) runs buddha 1: players drop to 1 HP at most and are reset to 100,
    // so nobody can die. That stays the default while every player has regeneration on. As soon as one
    // player turns it off, buddha (a global cheat convar) has to go off for everyone; the players
    // that still want regeneration are then healed to full every 0.1 s instead. That covers normal
    // damage, but a hit of more than 100 HP inside that 0.1 s (an AWP to the body without armor)
    // can kill.
    public partial class MatchZy
    {
        private bool humanRegenDefault = true;
        // Per-player choice that differs from humanRegenDefault, by userid.
        private readonly Dictionary<int, bool> humanRegenOverride = new();
        private bool botRegenEnabled = false;
        private CounterStrikeSharp.API.Modules.Timers.Timer? practiceRegenTimer = null;
        private const float PracticeRegenIntervalSeconds = 0.1f;

        [ConsoleCommand("css_liferegon", "Toggle your own health regeneration in practice")]
        public void OnLifeRegenConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleLifeRegenCommand(player, command.ArgString);

        public void HandleLifeRegenCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player) || !player!.UserId.HasValue)
                return;
            int uid = player.UserId.Value;
            if (!TryParseToggle(arg, HasHumanRegen(uid), out bool enabled))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".liferegon [true|false]"));
                return;
            }
            if (enabled == humanRegenDefault)
                humanRegenOverride.Remove(uid);
            else
                humanRegenOverride[uid] = enabled;
            ApplyPracticeHealthMode();
            if (enabled)
                HealToFull(player.PlayerPawn.Value);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, enabled ? "matchzy.px.liferegon" : "matchzy.px.liferegoff"));
        }

        [ConsoleCommand("css_allliferegon", "Set health regeneration for every player in practice")]
        public void OnAllLifeRegenConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleAllLifeRegenCommand(player, command.ArgString);

        public void HandleAllLifeRegenCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            // Explicit value only: a bare toggle is too easy to flip for everyone by accident.
            if (string.IsNullOrWhiteSpace(arg) || !TryParseToggle(arg, humanRegenDefault, out bool enabled))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".allliferegon <true|false>"));
                return;
            }
            humanRegenDefault = enabled;
            humanRegenOverride.Clear();
            ApplyPracticeHealthMode();
            string by = player!.PlayerName;
            foreach (var p in Utilities.GetPlayers())
            {
                if (!IsHumanPlayerValid(p))
                    continue;
                if (enabled)
                    HealToFull(p.PlayerPawn.Value);
                string state = Localizer.ForPlayer(p, enabled ? "matchzy.cc.enabled" : "matchzy.cc.disabled");
                PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.px.allliferegon", state, by));
            }
        }

        [ConsoleCommand("css_botlifereg", "Toggle health regeneration for practice bots")]
        public void OnBotLifeRegConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotLifeRegCommand(player, command.ArgString);

        public void HandleBotLifeRegCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (!TryParseToggle(arg, botRegenEnabled, out bool enabled))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".botlifereg [true|false]"));
                return;
            }
            botRegenEnabled = enabled;
            ApplyPracticeHealthMode();
            if (enabled)
            {
                foreach (var bot in TrackedPracticeBots())
                    if (bot.IsValid && bot.PawnIsAlive)
                        HealToFull(bot.PlayerPawn.Value);
            }
            ReplyToUserCommand(player, Localizer.ForPlayer(player, enabled ? "matchzy.px.botregon" : "matchzy.px.botregoff"));
        }

        private bool HasHumanRegen(int userId)
            => humanRegenOverride.TryGetValue(userId, out bool v) ? v : humanRegenDefault;

        // buddha only while every player has regeneration; otherwise the 0.1 s heal timer. The timer
        // also runs for .botlifereg (buddha_ignore_bots 1 keeps bots out of buddha).
        private void ApplyPracticeHealthMode()
        {
            if (!isPractice)
                return;
            bool everyoneRegen = humanRegenDefault && humanRegenOverride.Values.All(v => v);
            Server.ExecuteCommand(everyoneRegen ? "buddha 1" : "buddha 0");

            bool anyHumanRegen = humanRegenDefault || humanRegenOverride.Values.Any(v => v);
            bool needTimer = botRegenEnabled || (!everyoneRegen && anyHumanRegen);
            if (needTimer && practiceRegenTimer == null)
                practiceRegenTimer = AddTimer(PracticeRegenIntervalSeconds, PracticeRegenTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            else if (!needTimer)
                StopPracticeRegenTimer();
        }

        private void StopPracticeRegenTimer()
        {
            practiceRegenTimer?.Kill();
            practiceRegenTimer = null;
        }

        private void PracticeRegenTick()
        {
            try
            {
                if (!isPractice)
                {
                    StopPracticeRegenTimer();
                    return;
                }
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsHLTV || !p.PawnIsAlive || !p.UserId.HasValue)
                        continue;
                    if (p.TeamNum != (byte)CsTeam.Terrorist && p.TeamNum != (byte)CsTeam.CounterTerrorist)
                        continue;
                    bool heal = p.IsBot
                        ? botRegenEnabled && TrackedBotPosition(p.UserId.Value) != null
                        : HasHumanRegen(p.UserId.Value);
                    if (heal)
                        HealToFull(p.PlayerPawn.Value);
                }
            }
            catch (Exception e)
            {
                Log($"[PracticeRegen] {e.Message}");
            }
        }

        // Up to 100 HP. Never lowers health, so .god (huge health) is left alone.
        private static void HealToFull(CCSPlayerPawn? pawn)
        {
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0 || pawn.Health >= 100)
                return;
            pawn.Health = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        private void ResetPracticeHealth()
        {
            humanRegenDefault = true;
            humanRegenOverride.Clear();
            botRegenEnabled = false;
            StopPracticeRegenTimer();
        }
    }
}
