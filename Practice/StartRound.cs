using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // .startround: a fresh practice "round" for retake / execute reps. Thrown utility is cleared,
    // dead players and bots are respawned, every player is put on a different random spawn of their
    // side, bots go back to their placed spot with full health, and players are frozen for 5 seconds
    // with a countdown.
    //
    // This deliberately does not run mp_restartgame: practice lives in a paused warmup (prac.cfg) and
    // a real round restart would end the warmup and fight SettlePracticeWarmupState.
    public partial class MatchZy
    {
        private const int StartRoundFreezeSeconds = 5;
        private int startRoundGeneration = 0;
        private bool startRoundPending = false;
        // Pawns frozen by the current .startround, so only those are unfrozen again.
        private readonly HashSet<int> startRoundFrozen = new();

        [ConsoleCommand("css_startround", "Start a fresh practice round with a 5 second freeze")]
        public void OnStartRoundCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (isDryRun)
                return;
            if (startRoundPending)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.startroundbusy"));
                return;
            }
            startRoundPending = true;
            int generation = ++startRoundGeneration;
            // Respawns re-enter the engine: never on the chat/console command stack (rule 4).
            Server.NextFrame(() => BeginStartRound(generation));

            string by = player!.PlayerName;
            foreach (var p in Utilities.GetPlayers())
                if (IsHumanPlayerValid(p))
                    PrintToPlayerChat(p, Localizer.ForPlayer(p, "matchzy.px.startround", by, StartRoundFreezeSeconds));
        }

        private void BeginStartRound(int generation)
        {
            try
            {
                if (generation != startRoundGeneration || !isPractice)
                {
                    startRoundPending = false;
                    return;
                }
                RemoveGrenadeEntities();
                ResetBotCombatState();
                RerollBotJiggle();
                respawnAtSavedLocation.Clear();
                if (spawnsData.Values.Any(list => list.Count == 0))
                    GetSpawns();

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p == null || !p.IsValid || p.IsHLTV)
                        continue;
                    if (p.IsBot && (!p.UserId.HasValue || TrackedBotPosition(p.UserId.Value) == null))
                        continue;
                    RespawnIfDead(p);
                }

                // Give the respawns a moment to land (OnPlayerSpawn puts bots back on their spot), then place.
                AddTimer(0.2f, () => PlaceStartRoundPlayers(generation), TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception e)
            {
                Log($"[StartRound] {e.Message}");
                startRoundPending = false;
            }
        }

        private void PlaceStartRoundPlayers(int generation)
        {
            try
            {
                if (generation != startRoundGeneration || !isPractice)
                {
                    startRoundPending = false;
                    return;
                }
                var free = new Dictionary<byte, List<Position>>
                {
                    [(byte)CsTeam.Terrorist] = new List<Position>(spawnsData[(byte)CsTeam.Terrorist]),
                    [(byte)CsTeam.CounterTerrorist] = new List<Position>(spawnsData[(byte)CsTeam.CounterTerrorist]),
                };
                startRoundFrozen.Clear();

                foreach (var p in Utilities.GetPlayers())
                {
                    if (!IsPlayerValid(p) || !p.PawnIsAlive || !p.UserId.HasValue)
                        continue;
                    var pawn = p.PlayerPawn.Value!;
                    if (p.IsBot)
                    {
                        Position? spot = TrackedBotPosition(p.UserId.Value);
                        if (spot == null)
                            continue;
                        pawn.Teleport(spot.PlayerPosition, spot.PlayerAngle, new Vector(0, 0, 0));
                        HealToFull(pawn);
                        continue;
                    }
                    if (p.IsHLTV || !free.TryGetValue(p.TeamNum, out var spawns))
                        continue;
                    if (spawns.Count > 0)
                    {
                        int pick = Random.Shared.Next(spawns.Count);
                        Position spawn = spawns[pick];
                        spawns.RemoveAt(pick);
                        TeleportUpright(p, spawn.PlayerPosition, spawn.PlayerAngle);
                    }
                    HealToFull(pawn);
                    pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                    startRoundFrozen.Add(p.UserId.Value);
                }

                for (int left = StartRoundFreezeSeconds; left >= 1; left--)
                {
                    int secondsLeft = left;
                    AddTimer(StartRoundFreezeSeconds - left, () => StartRoundCountdown(generation, secondsLeft), TimerFlags.STOP_ON_MAPCHANGE);
                }
                AddTimer(StartRoundFreezeSeconds, () => EndStartRoundFreeze(generation), TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception e)
            {
                Log($"[StartRound] {e.Message}");
                startRoundPending = false;
            }
        }

        private void StartRoundCountdown(int generation, int secondsLeft)
        {
            if (generation != startRoundGeneration || !isPractice)
                return;
            foreach (var p in Utilities.GetPlayers())
                if (IsHumanPlayerValid(p))
                    p.PrintToCenter(Localizer.ForPlayer(p, "matchzy.px.startroundcount", secondsLeft));
        }

        private void EndStartRoundFreeze(int generation)
        {
            if (generation != startRoundGeneration)
                return;
            UnfreezeStartRoundPlayers();
            startRoundPending = false;
            if (!isPractice)
                return;
            foreach (var p in Utilities.GetPlayers())
                if (IsHumanPlayerValid(p))
                    p.PrintToCenter(Localizer.ForPlayer(p, "matchzy.px.startroundgo"));
        }

        private void UnfreezeStartRoundPlayers()
        {
            foreach (int uid in startRoundFrozen)
            {
                var p = Utilities.GetPlayerFromUserid(uid);
                var pawn = p?.PlayerPawn?.Value;
                if (p == null || !p.IsValid || pawn == null || !pawn.IsValid)
                    continue;
                // Only undo our own freeze (a .noclip or another freeze since then stays).
                if (pawn.MoveType != MoveType_t.MOVETYPE_NONE)
                    continue;
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }
            startRoundFrozen.Clear();
        }

        private void ResetStartRound()
        {
            startRoundGeneration++;
            UnfreezeStartRoundPlayers();
            startRoundPending = false;
        }
    }
}
