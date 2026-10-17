using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;

namespace MatchZy
{
    public partial class MatchZy
    {
        // Set by .1v1 without a match config. A 1v1 pug runs in the wingman game mode like a
        // wingman pug, so this flag is what picks live_1v1.cfg over live_wingman.cfg. It survives
        // the map change (ResetMatch does not touch it); loading a match config clears it.
        public bool pugOneVsOneMode;

        [ConsoleCommand("css_1v1", "Switch the server to 1v1 (wingman game mode, live_1v1.cfg): css_1v1 [map]")]
        public void On1v1Command(CCSPlayerController? player, CommandInfo? command)
            => HandleGameModeCommand(player, "1v1", command?.GetArg(1) ?? "");

        [ConsoleCommand("css_wingman", "Switch the server to wingman (2v2): css_wingman [map]")]
        [ConsoleCommand("css_2v2", "Switch the server to wingman (2v2): css_2v2 [map]")]
        public void OnWingmanCommand(CCSPlayerController? player, CommandInfo? command)
            => HandleGameModeCommand(player, "wingman", command?.GetArg(1) ?? "");

        [ConsoleCommand("css_5v5", "Switch the server to competitive (5v5): css_5v5 [map]")]
        [ConsoleCommand("css_competitive", "Switch the server to competitive (5v5): css_competitive [map]")]
        public void OnCompetitiveCommand(CCSPlayerController? player, CommandInfo? command)
            => HandleGameModeCommand(player, "competitive", command?.GetArg(1) ?? "");

        // Players per side in a pug, from the server's game mode: 1 for a .1v1 pug, 2 for wingman,
        // 3 for rush. 0 for competitive and for a loaded match (its own team size applies).
        private int PugModeTeamSize()
        {
            if (isMatchSetup || GetGameType() != 0)
                return 0;
            int gameMode = GetGameMode();
            if (gameMode == GameModeWingman)
                return pugOneVsOneMode ? 1 : 2;
            if (gameMode == GameModeRush)
                return 3;
            return 0;
        }

        // matchzy_minimum_ready_required capped at the pug mode's player count (2 for 1v1, 4 for
        // wingman, 6 for rush), so a pug in a small mode starts without .readyrequired. The admin's
        // value is kept as it is: a lower value still wins, and 5v5 is untouched. 0 (everyone
        // connected) stays 0.
        public int EffectiveMinimumReadyRequired()
        {
            int size = PugModeTeamSize();
            return size > 0 && minimumReadyRequired > 0 ? Math.Min(minimumReadyRequired, size * 2) : minimumReadyRequired;
        }

        // matchzy_ready_per_team capped the same way, at the pug mode's players per side.
        public int EffectiveReadyPerTeam()
        {
            int size = PugModeTeamSize();
            int perTeam = readyPerTeam.Value;
            return size > 0 && perTeam > 0 ? Math.Min(perTeam, size) : perTeam;
        }

        // Chat words for the mode commands, with or without a map argument.
        private static readonly Dictionary<string, string> GameModeChatCommands = new()
        {
            { ".1v1", "1v1" },
            { ".wingman", "wingman" },
            { ".2v2", "wingman" },
            { ".5v5", "competitive" },
            { ".competitive", "competitive" },
        };

        private static string GameModeLabel(string mode) => mode switch
        {
            "1v1" => "1v1",
            "wingman" => "Wingman (2v2)",
            _ => "Competitive (5v5)",
        };

        // game_mode only takes effect on a map load, so a mode change reloads the map: the one given,
        // else the current one. When the server is already in the right game_mode and no map is
        // given, only the pug's 1v1 flag changes (1v1 and wingman share game_mode 2) and the live
        // cfg follows it when the match goes live.
        private void HandleGameModeCommand(CCSPlayerController? player, string mode, string mapArg)
        {
            if (!IsPlayerAdmin(player, "css_map", "@css/map"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (GuardAgainstDryRun(player))
                return;
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.utility.matchstarted"));
                return;
            }

            int targetMode = GameModeValue(mode);
            mapArg = (mapArg ?? string.Empty).Trim();
            bool reloadRequired = GetGameMode() != targetMode || GetGameType() != 0;

            if (mapArg.Length == 0 && !reloadRequired)
            {
                pugOneVsOneMode = mode == "1v1";
                PrintLocalizedToAll("matchzy.gamemode.set", GameModeLabel(mode));
                return;
            }

            string map = mapArg.Length > 0 ? mapArg : Server.MapName;
            HandleMapChangeCommand(player, map, () =>
            {
                pugOneVsOneMode = mode == "1v1";
                ConVar.Find("game_type")?.SetValue(0);
                ConVar.Find("game_mode")?.SetValue(targetMode);
                Log($"[GameMode] Switching to {mode} (game_type 0, game_mode {targetMode}) on {map}");
                PrintLocalizedToAll("matchzy.gamemode.switching", GameModeLabel(mode));
            });
        }
    }
}
