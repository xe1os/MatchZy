using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public static class PlayerTeleport
{
    public static bool TeleportSafely(CCSPlayerController? player, Vector position, QAngle savedViewAngle)
    {
        if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
        {
            return false;
        }

        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        QAngle bodyAngle = new(0.0f, savedViewAngle.Y, 0.0f);
        Vector zeroVelocity = new(0.0f, 0.0f, 0.0f);

        pawn.Teleport(position, bodyAngle, zeroVelocity);

        pawn.EyeAngles.X = savedViewAngle.X;
        pawn.EyeAngles.Y = savedViewAngle.Y;
        pawn.EyeAngles.Z = 0.0f;

        return true;
    }
}
