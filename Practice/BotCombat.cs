using System.Globalization;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice bots that fight back.
    //   .botshoot [true|false]       - toggle: tracked practice bots stay on their placed spot, aim at
    //                                  the nearest visible enemy and fire after the reaction delay.
    //   .botreactiontime <0-1000>    - delay in ms between a bot first seeing a target and firing.
    //
    // The bot AI has to stay awake (bot_stop/bot_freeze/bot_zombie 0) so the engine keeps updating
    // its enemy visibility. Every tick the plugin then clears the AI's movement input and puts the bot
    // back on its spot, so it never walks. Native firing stays off (bot_dont_shoot 1); the plugin
    // presses attack itself once the reaction delay has passed. Active smokes block the sightline:
    // the engine can report an enemy as visible through smoke.
    public partial class MatchZy
    {
        private bool botShootEnabled = false;
        private int botReactionTimeMs = 500;
        // Per bot userid: the target it is looking at and since when it has been visible.
        private readonly Dictionary<int, (uint Target, float VisibleSince)> botReactionStates = new();
        // Bots currently holding +attack, so -attack is only sent on a change.
        private readonly HashSet<int> botsAttacking = new();
        // Bots that see a target right now. The jiggle tick leaves these alone (they stand still and shoot).
        private readonly HashSet<int> botsEngaged = new();
        private readonly List<Vector> activeSmokeCenters = new();
        private float nextSmokeRefreshTime;
        // Whether the awake-AI convars are applied. AddBot puts the AI back to sleep for every new
        // bot, so the tick re-applies them once the spawn is done.
        private bool botAiAwake;
        private readonly List<CCSPlayerPawn> tickTargets = new();
        private readonly Dictionary<int, (float Pitch, float Yaw)> botLastAim = new();
        private float nextBotUtilityStripTime;

        // The awake AI throws whatever grenades it carries (prac.cfg hands everyone a full set), so
        // shooting bots get their utility taken away.
        private static readonly string[] BotUtilityWeapons =
        {
            "weapon_flashbang", "weapon_hegrenade", "weapon_smokegrenade", "weapon_molotov",
            "weapon_incgrenade", "weapon_decoy", "weapon_tagrenade",
        };

        private const float BotSmokeBlockRadius = 144.0f;
        private const float BotSmokeRefreshSeconds = 0.1f;

        [ConsoleCommand("css_botshoot", "Toggle practice bots shooting back")]
        public void OnBotShootConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotShootCommand(player, command.ArgString);

        public void HandleBotShootCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (!TryParseToggle(arg, botShootEnabled, out bool enabled))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".botshoot [true|false]"));
                return;
            }
            botShootEnabled = enabled;
            ResetBotCombatState();
            ApplyPracticeBotAiConvars();
            ReplyToUserCommand(player, Localizer.ForPlayer(player, enabled ? "matchzy.px.botshooton" : "matchzy.px.botshootoff", botReactionTimeMs));
        }

        [ConsoleCommand("css_botreactiontime", "Practice bot reaction time in ms (0-1000)")]
        public void OnBotReactionTimeConsoleCommand(CCSPlayerController? player, CommandInfo command)
            => HandleBotReactionTimeCommand(player, command.ArgString);

        public void HandleBotReactionTimeCommand(CCSPlayerController? player, string arg)
        {
            if (!RequirePractice(player) || !IsPlayerValid(player))
                return;
            if (!int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) || ms < 0 || ms > 1000)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".botreactiontime <0-1000>"));
                return;
            }
            botReactionTimeMs = ms;
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.px.botreaction", ms));
        }

        // Bot AI convars for the current practice state. While a bot is being added the AI stays asleep
        // (a fresh bot is alive before SpawnBot claims it and could otherwise run off or throw its
        // default utility); SpawnBot calls this again once it is done.
        public void ApplyPracticeBotAiConvars()
        {
            if (!isPractice)
                return;
            botAiAwake = botShootEnabled && !isSpawningBot;
            if (botAiAwake)
                Server.ExecuteCommand("bot_difficulty 3; bot_dont_shoot 1; bot_stop 0; bot_zombie 0; bot_freeze 0");
            else
                Server.ExecuteCommand("bot_stop 1; bot_freeze 1; bot_zombie 1");
        }

        // Releases every held trigger and forgets reaction timers. Called on toggle, .startround and
        // when practice ends.
        private void ResetBotCombatState()
        {
            foreach (var bot in TrackedPracticeBots())
            {
                if (!bot.IsValid)
                    continue;
                var botState = bot.PlayerPawn.Value?.Bot;
                if (botState != null)
                    botState.ButtonFlags &= ~(ulong)PlayerButtons.Attack;
                if (bot.UserId.HasValue && botsAttacking.Contains(bot.UserId.Value))
                    bot.ExecuteClientCommandFromServer("-attack");
            }
            botReactionStates.Clear();
            botsAttacking.Clear();
            botsEngaged.Clear();
            botLastAim.Clear();
            activeSmokeCenters.Clear();
            nextSmokeRefreshTime = 0.0f;
            nextBotUtilityStripTime = 0.0f;
        }

        private void ForgetBotCombatState(int botUserId)
        {
            botReactionStates.Remove(botUserId);
            botsAttacking.Remove(botUserId);
            botsEngaged.Remove(botUserId);
            botLastAim.Remove(botUserId);
            _botJiggleRoll.Remove(botUserId);
        }

        // Snapshot of the tracked practice bot controllers (copied under the lock: the dictionary is
        // also written from timers).
        private List<CCSPlayerController> TrackedPracticeBots()
        {
            var result = new List<CCSPlayerController>();
            lock (_botsDictLock)
            {
                foreach (var data in pracUsedBots.Values)
                {
                    if (data.TryGetValue("controller", out var c) && c is CCSPlayerController bot)
                        result.Add(bot);
                }
            }
            return result;
        }

        private Position? TrackedBotPosition(int userId)
        {
            lock (_botsDictLock)
            {
                if (pracUsedBots.TryGetValue(userId, out var data) && data.TryGetValue("position", out var p) && p is Position pos)
                    return pos;
            }
            return null;
        }

        // OnTick. No-op unless practice is on and .botshoot is enabled.
        public void OnBotCombatTick()
        {
            if (!isPractice || !botShootEnabled)
                return;
            if (isSpawningBot)
            {
                botAiAwake = false;
                return;
            }
            try
            {
                if (!botAiAwake)
                    ApplyPracticeBotAiConvars();
                bool stripUtility = Server.CurrentTime >= nextBotUtilityStripTime;
                if (stripUtility)
                    nextBotUtilityStripTime = Server.CurrentTime + 0.5f;
                var smokes = RefreshSmokeCenters();
                // Living T/CT pawns, collected once per tick for every bot's target search.
                tickTargets.Clear();
                foreach (var candidate in Utilities.GetPlayers())
                {
                    if (candidate == null || !candidate.IsValid || candidate.IsHLTV || !candidate.PawnIsAlive)
                        continue;
                    var cp = candidate.PlayerPawn.Value;
                    if (cp != null && cp.IsValid && cp.AbsOrigin != null)
                        tickTargets.Add(cp);
                }
                foreach (var bot in TrackedPracticeBots())
                {
                    if (!IsPlayerValid(bot) || !bot.PawnIsAlive || !bot.UserId.HasValue)
                        continue;
                    var pawn = bot.PlayerPawn.Value;
                    var botState = pawn?.Bot;
                    if (pawn == null || botState == null || pawn.AbsOrigin == null)
                        continue;
                    int uid = bot.UserId.Value;
                    // A bot walking a recorded path is moved by the replay tick.
                    if (IsBotReplayingPath(uid))
                        continue;
                    Position? anchor = TrackedBotPosition(uid);
                    if (anchor == null)
                        continue;

                    // The AI is awake: wipe its movement input every tick so it never walks off.
                    ClearBotLocomotion(pawn, botState);
                    if (stripUtility)
                        foreach (string weapon in BotUtilityWeapons)
                            RemoveWeaponByName(bot, weapon);
                    bool jiggling = IsBotJiggling(uid);

                    CCSPlayerPawn? target = FindBotTarget(bot, botState, tickTargets);
                    Vector? targetOrigin = target?.AbsOrigin;
                    if (target == null || targetOrigin == null)
                    {
                        botsEngaged.Remove(uid);
                        SetBotAttacking(bot, botState, false);
                        botReactionStates.Remove(uid);
                        if (!jiggling)
                            pawn.Teleport(anchor.PlayerPosition, null, new Vector(0, 0, 0));
                        continue;
                    }

                    uint targetHandle = target.EntityHandle.Raw;
                    bool wasCurrentEnemy = botState.Enemy.Raw == targetHandle;
                    botState.Enemy.Raw = targetHandle;

                    Vector eye = botState.EyePosition;
                    float tx = targetOrigin.X + target.ViewOffset.X;
                    float ty = targetOrigin.Y + target.ViewOffset.Y;
                    float tz = targetOrigin.Z + target.ViewOffset.Z;
                    float dx = tx - eye.X, dy = ty - eye.Y, dz = tz - eye.Z;
                    float flat = MathF.Sqrt(dx * dx + dy * dy);
                    float pitch = -MathF.Atan2(dz, flat) * 180.0f / MathF.PI;
                    float yaw = MathF.Atan2(dy, dx) * 180.0f / MathF.PI;

                    bool visible = wasCurrentEnemy && botState.IsEnemyVisible
                        && !IsSightLineSmoked(eye.X, eye.Y, eye.Z, tx, ty, tz, smokes);

                    if (visible)
                    {
                        // Stand still where it is (also mid-jiggle) and face the target.
                        botsEngaged.Add(uid);
                        pawn.Teleport(jiggling ? null : anchor.PlayerPosition, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
                    }
                    else
                    {
                        botsEngaged.Remove(uid);
                        if (!jiggling)
                            pawn.Teleport(anchor.PlayerPosition, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
                    }
                    // setang is a client command per bot: only send it when the aim actually moved.
                    if (!botLastAim.TryGetValue(uid, out var lastAim)
                        || Math.Abs(lastAim.Pitch - pitch) > 0.5f || Math.Abs(lastAim.Yaw - yaw) > 0.5f)
                    {
                        botLastAim[uid] = (pitch, yaw);
                        bot.ExecuteClientCommandFromServer(string.Create(CultureInfo.InvariantCulture, $"setang {pitch:R} {yaw:R} 0"));
                    }
                    UpdateBotReaction(bot, botState, uid, targetHandle, visible);
                }
            }
            catch (Exception e)
            {
                Log($"[BotShoot] {e.Message}");
            }
        }

        private void UpdateBotReaction(CCSPlayerController bot, CCSBot botState, int uid, uint targetHandle, bool visible)
        {
            if (!visible)
            {
                botReactionStates.Remove(uid);
                SetBotAttacking(bot, botState, false);
                return;
            }
            float now = Server.CurrentTime;
            if (!botReactionStates.TryGetValue(uid, out var state) || state.Target != targetHandle)
            {
                botReactionStates[uid] = (targetHandle, now);
                SetBotAttacking(bot, botState, botReactionTimeMs == 0);
                return;
            }
            SetBotAttacking(bot, botState, now - state.VisibleSince >= botReactionTimeMs / 1000.0f);
        }

        private void SetBotAttacking(CCSPlayerController bot, CCSBot botState, bool attacking)
        {
            if (!bot.UserId.HasValue)
                return;
            int uid = bot.UserId.Value;
            if (attacking)
            {
                botState.ButtonFlags |= (ulong)PlayerButtons.Attack;
                if (botsAttacking.Add(uid))
                    bot.ExecuteClientCommandFromServer("+attack");
                return;
            }
            botState.ButtonFlags &= ~(ulong)PlayerButtons.Attack;
            if (botsAttacking.Remove(uid))
                bot.ExecuteClientCommandFromServer("-attack");
        }

        // The bot's current enemy if still valid, else the nearest living enemy player.
        private static CCSPlayerPawn? FindBotTarget(CCSPlayerController bot, CCSBot botState, List<CCSPlayerPawn> candidates)
        {
            CCSPlayerPawn? current = botState.Enemy.Value;
            if (IsValidBotTarget(bot, current))
                return current;

            Vector? origin = bot.PlayerPawn.Value?.AbsOrigin;
            if (origin == null)
                return null;
            CCSPlayerPawn? best = null;
            float bestDistSq = float.MaxValue;
            foreach (var pawn in candidates)
            {
                if (!IsValidBotTarget(bot, pawn) || pawn.AbsOrigin == null)
                    continue;
                float dx = pawn.AbsOrigin.X - origin.X, dy = pawn.AbsOrigin.Y - origin.Y, dz = pawn.AbsOrigin.Z - origin.Z;
                float distSq = dx * dx + dy * dy + dz * dz;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = pawn;
                }
            }
            return best;
        }

        private static bool IsValidBotTarget(CCSPlayerController bot, CCSPlayerPawn? pawn)
        {
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return false;
            byte team = pawn.TeamNum;
            return team != bot.TeamNum && (team == (byte)CsTeam.Terrorist || team == (byte)CsTeam.CounterTerrorist);
        }

        private List<Vector> RefreshSmokeCenters()
        {
            if (Server.CurrentTime < nextSmokeRefreshTime)
                return activeSmokeCenters;
            nextSmokeRefreshTime = Server.CurrentTime + BotSmokeRefreshSeconds;
            activeSmokeCenters.Clear();
            foreach (var smoke in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile"))
            {
                if (!smoke.IsValid || !smoke.DidSmokeEffect)
                    continue;
                Vector c = smoke.SmokeDetonationPos;
                activeSmokeCenters.Add(new Vector(c.X, c.Y, c.Z));
            }
            return activeSmokeCenters;
        }

        // True when the segment start -> end passes within BotSmokeBlockRadius of an active smoke.
        private static bool IsSightLineSmoked(float sx, float sy, float sz, float ex, float ey, float ez, List<Vector> smokes)
        {
            if (smokes.Count == 0)
                return false;
            float lx = ex - sx, ly = ey - sy, lz = ez - sz;
            float lenSq = lx * lx + ly * ly + lz * lz;
            if (lenSq <= float.Epsilon)
                return false;
            float radiusSq = BotSmokeBlockRadius * BotSmokeBlockRadius;
            foreach (var c in smokes)
            {
                float t = Math.Clamp(((c.X - sx) * lx + (c.Y - sy) * ly + (c.Z - sz) * lz) / lenSq, 0.0f, 1.0f);
                float px = sx + lx * t - c.X, py = sy + ly * t - c.Y, pz = sz + lz * t - c.Z;
                if (px * px + py * py + pz * pz <= radiusSq)
                    return true;
            }
            return false;
        }

        private static void ClearBotLocomotion(CCSPlayerPawn pawn, CCSBot botState)
        {
            botState.ForwardSpeed = 0.0f;
            botState.LeftSpeed = 0.0f;
            botState.VerticalSpeed = 0.0f;
            botState.IsRunning = false;
            botState.ButtonFlags &= ~((ulong)PlayerButtons.Forward | (ulong)PlayerButtons.Back
                | (ulong)PlayerButtons.Left | (ulong)PlayerButtons.Right
                | (ulong)PlayerButtons.Moveleft | (ulong)PlayerButtons.Moveright | (ulong)PlayerButtons.Jump);
            if (pawn.MovementServices == null || pawn.MovementServices.Handle == IntPtr.Zero)
                return;
            var movement = new CCSPlayer_MovementServices(pawn.MovementServices.Handle);
            movement.CmdForwardMove = 0.0f;
            movement.CmdLeftMove = 0.0f;
            movement.CmdUpMove = 0.0f;
            movement.ForwardMove = 0.0f;
            movement.LeftMove = 0.0f;
            movement.UpMove = 0.0f;
            try
            {
                // Without this the legs keep playing the walk animation in place.
                ResetBotMoveAnimation(movement);
            }
            catch
            {
                // Older CounterStrikeSharp builds have no CCSPlayerAnimationState: skip the cosmetic part.
            }
        }

        // Own NoInlining method: on a CounterStrikeSharp build without CCSPlayerAnimationState only
        // this call fails (caught above), not the JIT of the whole locomotion reset.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResetBotMoveAnimation(CCSPlayer_MovementServices movement)
        {
            var anim = movement.AnimationState;
            anim.PreviousHorizontalSpeed = 0.0f;
            anim.WasStationaryLastUpdate = true;
            anim.GroundMoveState = CCSPlayerAnimationStateGroundMoveState_t.Idle;
        }

        // "" toggles; true/false, on/off and 1/0 set the value.
        private static bool TryParseToggle(string arg, bool current, out bool value)
        {
            string a = (arg ?? "").Trim().Trim('"').ToLowerInvariant();
            switch (a)
            {
                case "":
                    value = !current;
                    return true;
                case "true":
                case "on":
                case "1":
                    value = true;
                    return true;
                case "false":
                case "off":
                case "0":
                    value = false;
                    return true;
                default:
                    value = current;
                    return false;
            }
        }
    }
}
