using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // .showrec: draw your current path (.rec or .loadrec) on the map, only for you.
    //   - the route as a line: white where you stood or walked, blue where you crouched,
    //   - a green post at the start, a red post at the end,
    //   - a post in the grenade's color at every throw spot.
    // .showrec again hides it. A new .rec / .loadrec redraws it while it is shown.
    // Beams are networked entities every client draws, so the line is capped at MaxRecDisplaySegments
    // and a CheckTransmit listener (registered only while a path is shown) hides each player's path
    // from everyone else.
    public partial class MatchZy
    {
        private const int MaxRecDisplaySegments = 64;
        private const float RecDisplayPostHeight = 72.0f;

        // Per player steamid: the beams of their shown path.
        private readonly Dictionary<ulong, List<CBeam>> _recDisplays = new();
        private Listeners.CheckTransmit? _recTransmitHandler;

        [ConsoleCommand("css_showrec", "Show or hide your recorded path on the map")]
        public void OnShowRecConsoleCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            ulong steamId = player!.SteamID;
            if (_recDisplays.ContainsKey(steamId))
            {
                HideRecDisplay(steamId);
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.showrecoff"));
                return;
            }
            if (!lastPathRecordings.TryGetValue(steamId, out var recording) || !IsSameMap(recording.MapName, Server.MapName))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.norecording"));
                return;
            }
            DrawRecDisplay(steamId, recording);
            ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.rp.showrecon"));
        }

        // A new .rec / .loadrec while the path is shown: draw the new one instead.
        private void RefreshRecDisplay(ulong steamId)
        {
            if (!_recDisplays.ContainsKey(steamId))
                return;
            HideRecDisplay(steamId);
            if (lastPathRecordings.TryGetValue(steamId, out var recording))
                DrawRecDisplay(steamId, recording);
        }

        private void DrawRecDisplay(ulong steamId, PathRecording recording)
        {
            HideRecDisplay(steamId);
            var beams = new List<CBeam>();
            var frames = recording.Frames;
            int step = Math.Max(1, (frames.Count - 1 + MaxRecDisplaySegments - 1) / MaxRecDisplaySegments);
            for (int i = 0; i < frames.Count - 1; i += step)
            {
                int j = Math.Min(i + step, frames.Count - 1);
                var a = frames[i];
                var b = frames[j];
                // A little above the floor, so the line is not hidden in it.
                AddRecBeam(beams, new Vector(a.X, a.Y, a.Z + 4), new Vector(b.X, b.Y, b.Z + 4),
                    a.Duck ? Color.FromArgb(80, 140, 255) : Color.White, 1.5f);
            }
            var first = frames[0];
            var last = frames[^1];
            AddRecPost(beams, first.X, first.Y, first.Z, Color.LimeGreen);
            AddRecPost(beams, last.X, last.Y, last.Z, Color.Red);
            foreach (var (_, nade) in recording.Throws)
            {
                var p = nade.PlayerPosition;
                AddRecPost(beams, p.X, p.Y, p.Z, RecNadeColor(nade.Type));
            }
            _recDisplays[steamId] = beams;
            SetRecTransmitListener(true);
        }

        private static Color RecNadeColor(string type) => type switch
        {
            "smoke" => Color.FromArgb(120, 160, 255),
            "flash" => Color.FromArgb(255, 225, 90),
            "hegrenade" => Color.FromArgb(235, 70, 70),
            "molotov" or "incendiary" => Color.FromArgb(255, 130, 60),
            _ => Color.FromArgb(180, 180, 180),
        };

        private static void AddRecPost(List<CBeam> beams, float x, float y, float z, Color color)
            => AddRecBeam(beams, new Vector(x, y, z), new Vector(x, y, z + RecDisplayPostHeight), color, 3.0f);

        private static void AddRecBeam(List<CBeam> beams, Vector from, Vector to, Color color, float width)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
                return;
            beam.LifeState = 1;
            beam.Width = width;
            beam.Render = color;
            beam.EndPos.X = to.X;
            beam.EndPos.Y = to.Y;
            beam.EndPos.Z = to.Z;
            beam.Teleport(from, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.DispatchSpawn();
            beams.Add(beam);
        }

        private void HideRecDisplay(ulong steamId)
        {
            if (!_recDisplays.Remove(steamId, out var beams))
                return;
            foreach (var b in beams)
                SafeRemoveEntity(b, "showrec");
            if (_recDisplays.Count == 0)
                SetRecTransmitListener(false);
        }

        // Practice start/end: remove every shown path (the entities are still valid here).
        private void ClearRecDisplays()
        {
            foreach (ulong steamId in _recDisplays.Keys.ToList())
                HideRecDisplay(steamId);
        }

        // New map: the beams are gone with the old map, only forget the wrappers (no Remove, no
        // IsValid on freed memory).
        private void ForgetRecDisplaysOnMapStart()
        {
            _recDisplays.Clear();
            SetRecTransmitListener(false);
        }

        // Each player sees only their own path.
        private void OnRecCheckTransmit(CCheckTransmitInfoList infoList)
        {
            if (_recDisplays.Count == 0)
                return;
            try
            {
                foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
                {
                    if (player == null || !player.IsValid)
                        continue;
                    ulong viewer = player.SteamID;
                    var transmit = info.TransmitEntities;
                    foreach (var (owner, beams) in _recDisplays)
                    {
                        if (owner == viewer)
                            continue;
                        foreach (var b in beams)
                            if (b != null && b.IsValid)
                                transmit.Remove(b);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[PathReplay] showrec transmit: {e.Message}");
            }
        }

        private void SetRecTransmitListener(bool on)
        {
            if (on && _recTransmitHandler == null)
            {
                _recTransmitHandler = OnRecCheckTransmit;
                RegisterListener(_recTransmitHandler);
            }
            else if (!on && _recTransmitHandler != null)
            {
                RemoveListener(_recTransmitHandler);
                _recTransmitHandler = null;
            }
        }
    }
}
