using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public class GrenadeThrownData
{
    public Vector Position { get; private set; }

    public QAngle Angle { get; private set; }

    public Vector Velocity { get; private set; }

    public QAngle AngularVelocity { get; private set; }

    public Vector PlayerPosition { get; private set; }

    public QAngle PlayerAngle { get; private set; }

    public string Type { get; private set; }

    public DateTime ThrownTime { get; private set; }

    public float Delay { get; set; }

    public UInt16 ItemIndex { get; set; }

    public GrenadeThrownData(Vector nadePosition, QAngle nadeAngle, Vector nadeVelocity, QAngle nadeAngularVelocity, Vector playerPosition, QAngle playerAngle, string grenadeType, DateTime thrownTime, UInt16 itemIndex)
    {
        Position = new Vector(nadePosition.X, nadePosition.Y, nadePosition.Z);
        Angle = new QAngle(nadeAngle.X, nadeAngle.Y, nadeAngle.Z);
        Velocity = new Vector(nadeVelocity.X, nadeVelocity.Y, nadeVelocity.Z);
        AngularVelocity = new QAngle(nadeAngularVelocity.X, nadeAngularVelocity.Y, nadeAngularVelocity.Z);
        PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
        PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        Type = grenadeType;
        ThrownTime = thrownTime;
        Delay = 0;
        ItemIndex = itemIndex;
    }

    public void LoadPosition(CCSPlayerController player)
    {
        PlayerTeleport.TeleportSafely(player, PlayerPosition, PlayerAngle);
    }

    public void Throw(CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null || !playerPawn.IsValid)
        {
            return;
        }

        if (!Constants.NadeProjectileMap.ContainsKey(Type))
        {
            Console.WriteLine($"[MatchZy] Unknown Grenade: {Type}");
            return;
        }

        CBaseCSGrenadeProjectile? grenadeEntity = TryCreateNativeProjectile(playerPawn, player.TeamNum);
        grenadeEntity ??= CreateFallbackProjectile();
        if (grenadeEntity == null)
        {
            return;
        }

        grenadeEntity.InitialPosition.X = Position.X;
        grenadeEntity.InitialPosition.Y = Position.Y;
        grenadeEntity.InitialPosition.Z = Position.Z;
        grenadeEntity.OriginalSpawnLocation.X = Position.X;
        grenadeEntity.OriginalSpawnLocation.Y = Position.Y;
        grenadeEntity.OriginalSpawnLocation.Z = Position.Z;

        grenadeEntity.InitialVelocity.X = Velocity.X;
        grenadeEntity.InitialVelocity.Y = Velocity.Y;
        grenadeEntity.InitialVelocity.Z = Velocity.Z;

        grenadeEntity.AngVelocity.X = AngularVelocity.X;
        grenadeEntity.AngVelocity.Y = AngularVelocity.Y;
        grenadeEntity.AngVelocity.Z = AngularVelocity.Z;

        grenadeEntity.Teleport(Position, Angle, Velocity);
        grenadeEntity.Globalname = "custom";
        grenadeEntity.TeamNum = player.TeamNum;
        grenadeEntity.InitialTeamNum = player.TeamNum;
        grenadeEntity.ItemIndex = ItemIndex;
        grenadeEntity.IsLive = true;
        grenadeEntity.IsSmokeGrenade = Type == "smoke";
        grenadeEntity.Thrower.Raw = playerPawn.EntityHandle.Raw;
        grenadeEntity.OriginalThrower.Raw = playerPawn.EntityHandle.Raw;
        grenadeEntity.OwnerEntity.Raw = playerPawn.EntityHandle.Raw;
    }

    private CBaseCSGrenadeProjectile? TryCreateNativeProjectile(CCSPlayerPawn playerPawn, int teamNumber)
    {
        bool attemptedNativeCreation = false;
        var smokeFactory = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc;
        var heFactory = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc;
        var molotovFactory = GrenadeFunctions.CMolotovProjectile_CreateFunc;
        var decoyFactory = GrenadeFunctions.CDecoyProjectile_CreateFunc;

        try
        {
            CBaseCSGrenadeProjectile? grenadeEntity = null;
            switch (Type)
            {
                case "smoke" when smokeFactory != null:
                    attemptedNativeCreation = true;
                    grenadeEntity = smokeFactory.Invoke(
                        Position.Handle,
                        Angle.Handle,
                        Velocity.Handle,
                        AngularVelocity.Handle,
                        playerPawn.Handle,
                        ItemIndex,
                        teamNumber
                    );
                    break;
                case "hegrenade" when heFactory != null:
                    attemptedNativeCreation = true;
                    grenadeEntity = heFactory.Invoke(
                        Position.Handle,
                        Angle.Handle,
                        Velocity.Handle,
                        AngularVelocity.Handle,
                        playerPawn.Handle,
                        ItemIndex
                    );
                    break;
                case "molotov" when molotovFactory != null:
                    attemptedNativeCreation = true;
                    grenadeEntity = molotovFactory.Invoke(
                        Position.Handle,
                        Angle.Handle,
                        Velocity.Handle,
                        AngularVelocity.Handle,
                        playerPawn.Handle,
                        ItemIndex
                    );
                    break;
                case "decoy" when decoyFactory != null:
                    attemptedNativeCreation = true;
                    grenadeEntity = decoyFactory.Invoke(
                        Position.Handle,
                        Angle.Handle,
                        Velocity.Handle,
                        AngularVelocity.Handle,
                        playerPawn.Handle,
                        ItemIndex
                    );
                    break;
            }

            if (!attemptedNativeCreation)
            {
                return null;
            }

            if (grenadeEntity != null && grenadeEntity.IsValid)
            {
                return grenadeEntity;
            }

            GrenadeFunctions.DisableNativeFactory(Type, "the native call returned no valid projectile");
        }
        catch (Exception exception)
        {
            GrenadeFunctions.DisableNativeFactory(
                Type,
                $"the native call threw {exception.GetType().Name}: {exception.Message}"
            );
        }

        return null;
    }

    private CBaseCSGrenadeProjectile? CreateFallbackProjectile()
    {
        try
        {
            CBaseCSGrenadeProjectile? grenadeEntity = Type switch
            {
                "smoke" => Utilities.CreateEntityByName<CSmokeGrenadeProjectile>("smokegrenade_projectile"),
                "hegrenade" => Utilities.CreateEntityByName<CHEGrenadeProjectile>("hegrenade_projectile"),
                "molotov" => Utilities.CreateEntityByName<CMolotovProjectile>("molotov_projectile"),
                "decoy" => Utilities.CreateEntityByName<CDecoyProjectile>("decoy_projectile"),
                "flash" => Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile"),
                _ => null
            };

            if (grenadeEntity == null || !grenadeEntity.IsValid)
            {
                Console.WriteLine($"[MatchZy] CreateEntityByName could not create a valid {Type} projectile.");
                return null;
            }

            grenadeEntity.DispatchSpawn();
            return grenadeEntity;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[MatchZy] CreateEntityByName failed for the {Type} projectile: " +
                $"{exception.GetType().Name}: {exception.Message}"
            );
            return null;
        }
    }
}
