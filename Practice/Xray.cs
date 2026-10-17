using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // .xray: every living player and bot glows through walls (T orange, CT blue), for checking
    // lineups, angles and bot spots. Practice only, for everyone on the server.
    //
    // A pawn's own glow does not render, so each pawn gets a glowing copy of its model: an invisible
    // prop_dynamic follows the pawn and the glowing prop follows that one (the usual CS2 glow setup).
    // The props are removed with entity-IO Kill on death, disconnect and when .xray is turned off,
    // and added again on spawn.
    public partial class MatchZy
    {
        private bool xrayEnabled;
        // Pawn owner's userid -> (relay prop, glow prop).
        private readonly Dictionary<int, (CBaseModelEntity Relay, CBaseModelEntity Glow)> xrayGlows = new();

        private void RegisterXray()
        {
            RegisterEventHandler<EventPlayerSpawn>(OnXraySpawn);
            RegisterEventHandler<EventPlayerDeath>(OnXrayDeath);
            RegisterEventHandler<EventPlayerDisconnect>(OnXrayDisconnect);
        }

        [ConsoleCommand("css_xray", "Toggle xray (players glow through walls) in practice")]
        public void OnXrayConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            xrayEnabled = !xrayEnabled;
            // Off the command stack: creating and killing entities re-enters the engine.
            Server.NextFrame(() =>
            {
                if (xrayEnabled && isPractice)
                {
                    foreach (var p in Utilities.GetPlayers())
                        AddXrayGlow(p);
                }
                else
                {
                    RemoveAllXrayGlows();
                }
            });
            PrintLocalizedToAll(xrayEnabled ? "matchzy.px.xrayon" : "matchzy.px.xrayoff");
        }

        private void AddXrayGlow(CCSPlayerController player)
        {
            try
            {
                if (!IsPlayerValid(player) || !player.PawnIsAlive || !player.UserId.HasValue || player.IsHLTV)
                    return;
                if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                    return;
                var pawn = player.PlayerPawn.Value!;
                string? model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
                if (string.IsNullOrEmpty(model))
                    return;
                RemoveXrayGlow(player.UserId.Value);

                var relay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
                var glow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
                if (relay == null || glow == null)
                    return;

                relay.SetModel(model);
                relay.Spawnflags = 256u; // no collisions
                relay.RenderMode = RenderMode_t.kRenderNone;
                relay.DispatchSpawn();

                glow.SetModel(model);
                glow.Spawnflags = 256u;
                glow.DispatchSpawn();
                glow.Glow.GlowColorOverride = player.TeamNum == (byte)CsTeam.Terrorist
                    ? Color.FromArgb(255, 255, 165, 0)
                    : Color.FromArgb(255, 80, 160, 255);
                glow.Glow.GlowRange = 5000;
                glow.Glow.GlowRangeMin = 0;
                glow.Glow.GlowTeam = -1;
                glow.Glow.GlowType = 3;

                relay.AcceptInput("FollowEntity", pawn, relay, "!activator");
                glow.AcceptInput("FollowEntity", relay, glow, "!activator");
                xrayGlows[player.UserId.Value] = (relay, glow);
            }
            catch (Exception e)
            {
                Log($"[Xray] {e.Message}");
            }
        }

        private void RemoveXrayGlow(int userId)
        {
            if (!xrayGlows.Remove(userId, out var props))
                return;
            if (props.Glow.IsValid)
                props.Glow.AcceptInput("Kill");
            if (props.Relay.IsValid)
                props.Relay.AcceptInput("Kill");
        }

        private void RemoveAllXrayGlows()
        {
            foreach (int userId in xrayGlows.Keys.ToList())
                RemoveXrayGlow(userId);
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetXray()
        {
            xrayEnabled = false;
            RemoveAllXrayGlows();
        }

        private HookResult OnXraySpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            if (!xrayEnabled || !isPractice)
                return HookResult.Continue;
            var player = @event.Userid;
            // Next frame: the pawn is placed and has its model after the event.
            Server.NextFrame(() =>
            {
                if (xrayEnabled && isPractice && player != null)
                    AddXrayGlow(player);
            });
            return HookResult.Continue;
        }

        private HookResult OnXrayDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            if (xrayGlows.Count == 0)
                return HookResult.Continue;
            var victim = @event.Userid;
            if (victim != null && victim.UserId.HasValue)
            {
                int userId = victim.UserId.Value;
                Server.NextFrame(() => RemoveXrayGlow(userId));
            }
            return HookResult.Continue;
        }

        private HookResult OnXrayDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            if (xrayGlows.Count == 0)
                return HookResult.Continue;
            var player = @event.Userid;
            if (player != null && player.UserId.HasValue)
            {
                int userId = player.UserId.Value;
                Server.NextFrame(() => RemoveXrayGlow(userId));
            }
            return HookResult.Continue;
        }
    }
}
