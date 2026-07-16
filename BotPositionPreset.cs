using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public sealed class SavedBotPosition
{
    public byte TeamNum { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float ViewPitch { get; set; }
    public float ViewYaw { get; set; }
    public float ViewRoll { get; set; }
    public bool Crouched { get; set; }

    public Position ToPosition()
    {
        return new Position(
            new Vector(PositionX, PositionY, PositionZ),
            new QAngle(ViewPitch, ViewYaw, ViewRoll));
    }
}

public sealed class SavedBotPositionPreset
{
    public string Map { get; set; } = string.Empty;
    public List<SavedBotPosition> Bots { get; set; } = new();
}
