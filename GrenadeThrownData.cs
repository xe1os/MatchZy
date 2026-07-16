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
		if (playerPawn == null || !playerPawn.IsValid) return;

		CBaseCSGrenadeProjectile? grenadeEntity = null;
		switch (Type)
		{
			case "smoke":
			{
				if (GrenadeFunctions.IsLinux)
				{
					grenadeEntity = GrenadeFunctions.CSmokeGrenadeProjectile_CreateFunc!.Invoke(
						Position.Handle,
						Angle.Handle,
						Velocity.Handle,
						AngularVelocity.Handle,
						playerPawn.Handle,
						ItemIndex,
						player.TeamNum);
				}
				else
				{
					grenadeEntity = Utilities.CreateEntityByName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
					if (grenadeEntity == null) return;
					grenadeEntity.DispatchSpawn();
				}
				break;
			}
			case "molotov":
			{
				grenadeEntity = GrenadeFunctions.CMolotovProjectile_CreateFunc.Invoke(
					Position.Handle,
					Angle.Handle,
					Velocity.Handle,
					AngularVelocity.Handle,
					playerPawn.Handle,
					ItemIndex);
				break;
			}
			case "hegrenade":
			{
				grenadeEntity = GrenadeFunctions.CHEGrenadeProjectile_CreateFunc.Invoke(
					Position.Handle,
					Angle.Handle,
					Velocity.Handle,
					AngularVelocity.Handle,
					playerPawn.Handle,
					ItemIndex);
				break;
			}
			case "decoy":
			{
				grenadeEntity = GrenadeFunctions.CDecoyProjectile_CreateFunc.Invoke(
					Position.Handle,
					Angle.Handle,
					Velocity.Handle,
					AngularVelocity.Handle,
					playerPawn.Handle,
					ItemIndex);
				break;
			}
			case "flash":
			{
				grenadeEntity = Utilities.CreateEntityByName<CFlashbangProjectile>("flashbang_projectile");
				if (grenadeEntity == null) return;
				grenadeEntity.DispatchSpawn();
				break;
			}
			default:
				Console.WriteLine($"[MatchZy] Unknown Grenade: {Type}");
				break;
		}

		if (grenadeEntity != null)
		{
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
    }
}
