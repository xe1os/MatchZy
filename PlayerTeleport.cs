using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;

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

        // Keep the physical pawn upright, matching the spawn teleport behavior.
        pawn.Teleport(position, bodyAngle, zeroVelocity);

        // setang changes the player's view without applying pitch or roll to the
        // physical model. The command name is fixed and every argument is a
        // locally formatted numeric angle captured from the same player.
        string viewAngleCommand = string.Create(
            CultureInfo.InvariantCulture,
            $"setang {savedViewAngle.X:R} {savedViewAngle.Y:R} {savedViewAngle.Z:R}");
        player.ExecuteClientCommandFromServer(viewAngleCommand);

        return true;
    }
}
