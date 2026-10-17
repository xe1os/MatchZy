using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Practice utility toggles:
    //   .expire         - smokes and molotovs do not run out. Type it again to turn it off.
    //   .coloredsmokes  - smokes in the thrower's teammate color on / off (matchzy_smoke_color_enabled).
    //
    // Molotovs: inferno_flame_lifetime(_incendiary) is raised while .expire is on (prac.cfg runs
    // with sv_cheats) and put back afterwards; it applies to fires started from then on.
    // Smokes have no lifetime cvar: shortly before a smoke fades, a new one is popped at the same
    // spot, so the cloud never opens. That stops once the smoke is gone (.clear) or .expire is off.
    public partial class MatchZy
    {
        private bool utilityNoExpire;
        // The engine's flame lifetimes before .expire, put back when it is turned off.
        private float? savedFlameLifetime;
        private float? savedFlameLifetimeIncendiary;
        private int expireGeneration;

        // A smoke lasts about 20 s after it pops; the replacement pops before that.
        private const float SmokeRefreshSeconds = 15.0f;
        private const float ExpireFlameLifetime = 3600.0f;

        private void RegisterUtilityExpire()
        {
            RegisterEventHandler<EventSmokegrenadeDetonate>(OnExpireSmokeDetonate);
        }

        [ConsoleCommand("css_expire", "Practice: smokes and molotovs do not run out (toggle)")]
        public void OnExpireConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            SetUtilityNoExpire(!utilityNoExpire);
            PrintLocalizedToAll(utilityNoExpire ? "matchzy.px.expireon" : "matchzy.px.expireoff");
        }

        private void SetUtilityNoExpire(bool on)
        {
            if (on == utilityNoExpire)
                return;
            utilityNoExpire = on;
            expireGeneration++;
            var lifetime = ConVar.Find("inferno_flame_lifetime");
            var lifetimeInc = ConVar.Find("inferno_flame_lifetime_incendiary");
            if (on)
            {
                savedFlameLifetime = lifetime?.GetPrimitiveValue<float>();
                savedFlameLifetimeIncendiary = lifetimeInc?.GetPrimitiveValue<float>();
                Server.ExecuteCommand(string.Create(CultureInfo.InvariantCulture,
                    $"inferno_flame_lifetime {ExpireFlameLifetime}; inferno_flame_lifetime_incendiary {ExpireFlameLifetime}"));
            }
            else
            {
                if (savedFlameLifetime is float f)
                    Server.ExecuteCommand(string.Create(CultureInfo.InvariantCulture, $"inferno_flame_lifetime {f}"));
                if (savedFlameLifetimeIncendiary is float fi)
                    Server.ExecuteCommand(string.Create(CultureInfo.InvariantCulture, $"inferno_flame_lifetime_incendiary {fi}"));
                savedFlameLifetime = null;
                savedFlameLifetimeIncendiary = null;
            }
        }

        // Practice start and end (ResetPracticeExtras).
        private void ResetUtilityExpire() => SetUtilityNoExpire(false);

        private HookResult OnExpireSmokeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            if (!utilityNoExpire || !isPractice)
                return HookResult.Continue;
            try
            {
                int entityIndex = @event.Entityid;
                var smoke = Utilities.GetEntityFromIndex<CSmokeGrenadeProjectile>(entityIndex);
                if (smoke == null || !smoke.IsValid)
                    return HookResult.Continue;
                float x = @event.X, y = @event.Y, z = @event.Z;
                byte team = smoke.TeamNum;
                ushort itemIndex = smoke.ItemIndex;
                float r = smoke.SmokeColor.X, g = smoke.SmokeColor.Y, b = smoke.SmokeColor.Z;
                uint throwerRaw = smoke.Thrower.Raw;
                int generation = expireGeneration;
                AddTimer(SmokeRefreshSeconds, () =>
                {
                    if (!utilityNoExpire || !isPractice || generation != expireGeneration)
                        return;
                    // Gone (.clear, map change): let it end.
                    var old = Utilities.GetEntityFromIndex<CSmokeGrenadeProjectile>(entityIndex);
                    if (old == null || !old.IsValid || old.DesignerName != "smokegrenade_projectile")
                        return;
                    PopReplacementSmoke(x, y, z, team, itemIndex, r, g, b, throwerRaw);
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception e)
            {
                Log($"[Expire] {e.Message}");
            }
            return HookResult.Continue;
        }

        // A smoke at rest on the old one's spot pops at once; its own detonate event schedules the
        // next one, so the chain keeps going.
        private void PopReplacementSmoke(float x, float y, float z, byte team, ushort itemIndex, float r, float g, float b, uint throwerRaw)
        {
            var pos = new Vector(x, y, z + 2.0f);
            var ang = new QAngle(0, 0, 0);
            var vel = new Vector(0, 0, 0);
            var factory = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc;
            CSmokeGrenadeProjectile? smoke = factory?.Invoke(pos.Handle, ang.Handle, vel.Handle, vel.Handle, IntPtr.Zero, itemIndex == 0 ? 45 : itemIndex, team);
            if (smoke == null || !smoke.IsValid)
            {
                Log("[Expire] Could not create the replacement smoke (CSmokeGrenadeProjectile_Create missing).");
                return;
            }
            // Skipped by the grenade history (OnEntitySpawned ignores "custom").
            smoke.Globalname = "custom";
            smoke.TeamNum = team;
            smoke.Thrower.Raw = throwerRaw;
            smoke.OriginalThrower.Raw = throwerRaw;
            smoke.SmokeColor.X = r;
            smoke.SmokeColor.Y = g;
            smoke.SmokeColor.Z = b;
            smoke.Teleport(pos, ang, vel);
        }

        [ConsoleCommand("css_coloredsmokes", "Practice: colored smokes on / off")]
        public void OnColoredSmokesConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player))
                return;
            smokeColorEnabled.Value = !smokeColorEnabled.Value;
            PrintLocalizedToAll(smokeColorEnabled.Value ? "matchzy.px.coloredsmokeson" : "matchzy.px.coloredsmokesoff");
        }
    }
}
