using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.Globalization;
using System.Text.Json;


namespace MatchZy
{
    public class Position
    {

        public Vector PlayerPosition { get; private set; }
        public QAngle PlayerAngle { get; private set; }

        // Copy constructor
        public Position(Position other)
        {
            PlayerPosition = other.PlayerPosition;
            PlayerAngle = other.PlayerAngle;
        }

        public Position(Vector playerPosition, QAngle playerAngle)
        {
            // Create deep copies of the Vector and QAngle objects
            PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
            PlayerAngle = new QAngle(playerAngle.X, playerAngle.Y, playerAngle.Z);
        }

        public void Teleport(CCSPlayerController player)
        {
            PlayerTeleport.TeleportSafely(player, PlayerPosition, PlayerAngle);
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            Position otherPosition = (Position)obj;

            return PlayerPosition.X == otherPosition.PlayerPosition.X &&
                PlayerPosition.Y == otherPosition.PlayerPosition.Y &&
                PlayerAngle.X == otherPosition.PlayerAngle.X &&
                PlayerAngle.Y == otherPosition.PlayerAngle.Y &&
                PlayerAngle.Z == otherPosition.PlayerAngle.Z;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + PlayerPosition.X.GetHashCode();
                hash = hash * 23 + PlayerPosition.Y.GetHashCode();
                hash = hash * 23 + PlayerPosition.Z.GetHashCode();
                hash = hash * 23 + PlayerAngle.X.GetHashCode();
                hash = hash * 23 + PlayerAngle.Y.GetHashCode();
                hash = hash * 23 + PlayerAngle.Z.GetHashCode();
                return hash;
            }
        }
    }

    public static class StringSimilarity
    {
        // Dice coefficient function
        public static double DiceCoefficient(string s1, string s2)
        {
            var bigrams1 = GetBigrams(s1);
            var bigrams2 = GetBigrams(s2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            return (2.0 * intersection) / (bigrams1.Count + bigrams2.Count);
        }

        // Get bigrams function
        private static List<string> GetBigrams(string input)
        {
            var bigrams = new List<string>();
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Finds the name from a list of names that is nearest to the input name using the Dice coefficient.
        /// </summary>
        /// <param name="inputName">The input name to match.</param>
        /// <param name="names">The list of names to search from.</param>
        /// <returns>The nearest matching name from the list.</returns>
        public static string FindNearestName(string inputName, List<string> names)
        {
            if (inputName.Length == 1)
            {
                // If input name is a single character, find the name that starts with the same character
                var matchingName = names.FirstOrDefault(name => name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase));
                if (matchingName != null)
                {
                    return matchingName;
                }
            }
            // Otherwise, use the Dice coefficient to find the nearest name
            string nearestName = names.OrderByDescending(name => DiceCoefficient(inputName, name)).FirstOrDefault() ?? inputName;
            return nearestName;
        }
    }

    public partial class MatchZy
    {
        int maxLastGrenadesSavedLimit = 512;
        Dictionary<int, List<GrenadeThrownData>> lastGrenadesData = new();
        Dictionary<int, Dictionary<string, GrenadeThrownData>> nadeSpecificLastGrenadeData = new();
        Dictionary<int, DateTime> lastGrenadeThrownTime = new();
        Dictionary<int, DateTime> lastRethrowCommandTime = new();
        Dictionary<int, DateTime> lastGlobalRethrowCommandTime = new();
        Dictionary<int, PlayerPracticeTimer> playerTimers = new();
        Dictionary<int, PlayerLocationData> savedPlayerLocationData = new();
        readonly List<CBeam> spawnMarkerBeams = new();

        const float SpawnMarkerHalfSize = 18.0f;
        const float SpawnMarkerHeightOffset = 8.0f;
        const float SpawnMarkerWidth = 1.5f;
        const float SpawnMarkerVerticalTolerance = 36.0f;
        const int MinimumCompetitiveSpawnCount = 5;

        bool spawnMarkersEnabled;

        public Dictionary<byte, List<Position>> spawnsData = GetEmptySpawnsData();

        public Dictionary<byte, List<Position>> coachSpawns = GetEmptySpawnsData();

        public const string practiceCfgPath = "MatchZy/prac.cfg";
        public const string dryrunCfgPath = "MatchZy/dryrun.cfg";

        // This map stores the bots which are being used in prac (probably spawned using .bot). Key is the userid of the bot.
        public Dictionary<int, Dictionary<string, object>> pracUsedBots = new Dictionary<int, Dictionary<string, object>>();

        private CounterStrikeSharp.API.Modules.Timers.Timer? collisionGroupTimer;

        public bool isSpawningBot;
        private int botPresetLoadGeneration;
        private bool botShootingEnabled;
        private bool botRespawnEnabled = true;
        private bool botLifeRegenerationEnabled;
        private readonly HashSet<int> humanGodModeEnabled = new();
        private readonly HashSet<int> humanLifeRegenerationEnabled = new();
        private int botReactionTimeMs = DefaultBotReactionTimeMs;
        private bool practiceRoundTimeoutEnding;
        private readonly Dictionary<int, int> botHealthCeilings = new();
        private float botNextRegenerationTime;
        private readonly Dictionary<int, float> humanNextRegenerationTimes = new();
        private readonly Dictionary<int, (uint TargetHandle, float VisibleSince)> botReactionStates = new();
        private readonly HashSet<int> turretBotsAttacking = new();
        private readonly Dictionary<int, uint> disconnectingPracticeHumanPawnHandles = new();

        private const int DefaultBotReactionTimeMs = 500;
        private const int GodModeHealth = int.MaxValue / 2;
        private const float PracticeRespawnDelaySeconds = 0.5f;
        private const float PracticeSideInventoryUpdateDelaySeconds = PracticeRespawnDelaySeconds + 0.1f;
        private const float PracticeLifeRegenerationIntervalSeconds = 0.1f;

        public bool isDryRun = false;

        public List<int> noFlashList = new List<int>();

        public static Dictionary<byte, List<Position>> GetEmptySpawnsData()
        {
            return new Dictionary<byte, List<Position>>
            {
                { (byte)CsTeam.CounterTerrorist, new List<Position>() },
                { (byte)CsTeam.Terrorist, new List<Position>() }
            };
        }

        public void StartPracticeMode()
        {
            if (matchStarted) return;
            CloseAllConfigurationMenus();
            isPractice = true;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath)))
            {
                Log($"[StartWarmup] Starting Practice Mode! Executing Practice CFG from {practiceCfgPath}");
                Server.ExecuteCommand($"exec {practiceCfgPath}");
            }
            else
            {
                Log($"[StartWarmup] Starting Practice Mode! Practice CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("""sv_cheats "true"; mp_force_pick_time "0"; bot_quota "0"; sv_showimpacts "1"; mp_limitteams "0"; sv_deadtalk "true"; sv_full_alltalk "true"; sv_ignoregrenaderadio "false"; mp_forcecamera "0"; sv_grenade_trajectory_prac_pipreview "true"; sv_grenade_trajectory_prac_trailtime "3"; sv_infinite_ammo "1"; weapon_auto_cleanup_time "15"; weapon_max_before_cleanup "30"; mp_buy_anywhere "1"; mp_maxmoney "9999999"; mp_startmoney "9999999";""");
                Server.ExecuteCommand("""mp_weapons_allow_typecount "-1"; mp_death_drop_breachcharge "false"; mp_death_drop_defuser "false"; mp_death_drop_taser "false"; mp_drop_knife_enable "true"; mp_death_drop_grenade "0"; ammo_grenade_limit_total "5"; mp_defuser_allocation "2"; mp_free_armor "2"; mp_ct_default_grenades "weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_ct_default_primary "weapon_m4a1";""");
                Server.ExecuteCommand("""mp_t_default_grenades "weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_t_default_primary "weapon_ak47"; mp_warmup_online_enabled "true"; mp_warmup_pausetimer "1"; mp_warmup_start; bot_quota_mode fill; mp_solid_teammates 2; mp_autoteambalance false; mp_teammates_are_enemies false;""");
            }
            DisablePracticeTeamDamagePenalties();
            Server.NextFrame(DisablePracticeTeamDamagePenalties);
            botRespawnEnabled = true;
            botLifeRegenerationEnabled = false;
            ResetPracticeHumanGodModes();
            humanLifeRegenerationEnabled.Clear();
            botReactionTimeMs = DefaultBotReactionTimeMs;
            botHealthCeilings.Clear();
            botNextRegenerationTime = 0.0f;
            humanNextRegenerationTimes.Clear();
            ResetTurretCombatState();
            practiceRoundTimeoutEnding = false;
            // Respawns are handled per player so bot respawns can be toggled without affecting humans.
            Server.ExecuteCommand("mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0");
            // Human regeneration is managed per player by .liferegon. Disable the
            // server-wide mechanisms so players can take lethal damage normally.
            DisableEngineHumanHealthProtection();
            Server.NextFrame(DisableEngineHumanHealthProtection);
            foreach (CCSPlayerController player in Utilities.GetPlayers())
            {
                EnableDefaultHumanLifeRegeneration(player);
            }
            // Bot respawning is asynchronous, so practice mode must always suppress
            // elimination wins. The normal round timeout is enforced by the plugin.
            Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
            GetSpawns();
            InitializePracticeSpawnMarkers();
            PrintToAllChat($"Practice mode loaded!");
            Server.PrintToChatAll($" {ChatColors.Green}Configuration: {ChatColors.Default}.menu");
            Server.PrintToChatAll($" {ChatColors.Green}Spawns: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn");
            Server.PrintToChatAll($" {ChatColors.Green}Spawns: {ChatColors.Default}.spawnmarkers");
            Server.PrintToChatAll($" {ChatColors.Green}Bots: {ChatColors.Default}.bot, .nobots, .botshoot <true/false>, .botreactiontime <0-1000>, .botrespawn <true/false>, .botlifereg <true/false>, .crouchbot, .boost, .crouchboost");
            Server.PrintToChatAll($" {ChatColors.Green}Bots: {ChatColors.Default}.sbp <name>, .lbp <name>, .dbp <name>, .listbp");
            Server.PrintToChatAll($" {ChatColors.Green}Nades: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades");
            Server.PrintToChatAll($" {ChatColors.Green}Nade Throw: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>");
            Server.PrintToChatAll($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj");
            // On new line to prevent text cutting off
            Server.PrintToChatAll($" {ChatColors.Green}Locations: {ChatColors.Default}.slp, .tlp, .dlp, .savepos, .loadpos");
            Server.PrintToChatAll($" {ChatColors.Green}Health: {ChatColors.Default}.liferegon <true/false>");
            Server.PrintToChatAll($" {ChatColors.Green}Sides & Others: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .exitprac");
        }

        private void DisablePracticeTeamDamagePenalties()
        {
            if (!isPractice) return;

            Server.ExecuteCommand(
                "mp_autokick 0; mp_friendlyfire 1; mp_spawnprotectiontime 0; mp_td_dmgtokick 0; " +
                "mp_td_dmgtowarn 0; mp_td_spawndmgthreshold 0; mp_tkpunish 0");
        }

        public void GetSpawns()
        {
            // Resetting spawn data to avoid any glitches
            spawnsData = GetEmptySpawnsData();

            AddCompetitiveTeamSpawns("info_player_counterterrorist", (byte)CsTeam.CounterTerrorist);
            AddCompetitiveTeamSpawns("info_player_terrorist", (byte)CsTeam.Terrorist);

            Log($"[GetSpawns] Found {spawnsData[(byte)CsTeam.CounterTerrorist].Count} CT and {spawnsData[(byte)CsTeam.Terrorist].Count} T spawn positions on {Server.MapName}");

            GetCoachSpawns();
        }

        private void AddCompetitiveTeamSpawns(string designerName, byte teamNum)
        {
            List<(int Priority, Position Position)> availableSpawns = new();

            foreach (SpawnPoint spawn in Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(designerName))
            {
                if (!spawn.IsValid || !spawn.Enabled) continue;

                var sceneNode = spawn.CBodyComponent?.SceneNode;
                Vector? origin = sceneNode?.AbsOrigin;
                QAngle? rotation = sceneNode?.AbsRotation;
                if (origin == null || rotation == null) continue;

                availableSpawns.Add((
                    spawn.Priority,
                    new Position(
                        new Vector(origin.X, origin.Y, origin.Z),
                        new QAngle(rotation.X, rotation.Y, rotation.Z))));
            }

            if (availableSpawns.Count == 0) return;

            availableSpawns = availableSpawns.OrderBy(spawn => spawn.Priority).ToList();
            int primaryPriority = availableSpawns[0].Priority;

            // The map's lowest-priority group is its primary competitive set.
            // Preserve that complete group even when it contains more than five
            // spawns, then use higher-priority fallbacks only to reach 5v5.
            spawnsData[teamNum].AddRange(availableSpawns
                .Where(spawn => spawn.Priority == primaryPriority)
                .Select(spawn => spawn.Position));

            int missingSpawns = MinimumCompetitiveSpawnCount - spawnsData[teamNum].Count;
            if (missingSpawns > 0)
            {
                spawnsData[teamNum].AddRange(availableSpawns
                    .Where(spawn => spawn.Priority > primaryPriority)
                    .Take(missingSpawns)
                    .Select(spawn => spawn.Position));
            }
        }

        private void HandleSpawnCommand(CCSPlayerController? player, string commandArg, byte teamNum, string command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (teamNum != 2 && teamNum != 3) return;
            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int spawnNumber) && spawnNumber >= 1)
                {
                    // Adjusting the spawnNumber according to the array index.
                    spawnNumber -= 1;
                    if (spawnsData.ContainsKey(teamNum) && spawnsData[teamNum].Count <= spawnNumber) return;
                    PlayerTeleport.TeleportSafely(player, spawnsData[teamNum][spawnNumber].PlayerPosition, spawnsData[teamNum][spawnNumber].PlayerAngle);
                    // ReplyToUserCommand(player, $"Moved to spawn: {spawnNumber+1}/{spawnsData[teamNum].Count}");
                    ReplyToUserCommand(player, Localizer["matchzy.pm.movedtospawn", $"{spawnNumber + 1}/{spawnsData[teamNum].Count}"]);
                }
                else
                {
                    // ReplyToUserCommand(player, $"Invalid value for {command} command. Please specify a valid non-negative number. Usage: !{command} <number>");
                    ReplyToUserCommand(player, Localizer["matchzy.pm.negativenumber"]);
                    return;
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: !{command} <number>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!{command} <number>"]);
            }
        }

        private string GetNadeType(string nadeName)
        {
            switch (nadeName)
            {
                case "weapon_flashbang":
                    return "Flash";
                case "weapon_smokegrenade":
                    return "Smoke";
                case "weapon_hegrenade":
                    return "HE";
                case "weapon_decoy":
                    return "Decoy";
                case "weapon_molotov":
                    return "Molly";
                case "weapon_incgrenade":
                    return "Molly";
                default:
                    return "";
            }
        }

        private void HandleSaveNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Split string into 2 parts
                string[] lineupUserString = saveNadeName.Split(' ');
                string lineupName = lineupUserString[0];
                string lineupDesc = string.Join(" ", lineupUserString, 1, lineupUserString.Length - 1);

                // Get player info: steamid, pos, ang
                string playerSteamID;
                if(isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player!.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                QAngle playerAngle = player!.PlayerPawn.Value!.EyeAngles;
                Vector playerPos = player.Pawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                string currentMapName = Server.MapName;
                string nadeType = GetNadeType(player.PlayerPawn.Value.WeaponServices!.ActiveWeapon.Value!.DesignerName);

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                // Check if the file exists, if not, create it with an empty JSON object
                if (!File.Exists(savednadesPath))
                {
                    File.WriteAllText(savednadesPath, "{}");
                }

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup name already exists for the given SteamID
                    if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                    {
                        // Check if the lineup already exists on the same map
                        if (savedNadesDict[playerSteamID][lineupName]["Map"] == currentMapName)
                        {
                            // Lineup already exists on the same map, reply to the user and return
                            // ReplyToUserCommand(player, $"Lineup already exists! Please use a different name or use .delnade <nade>");
                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupissaved"]);
                            return;
                        }
                    }

                    // Update or add the new lineup information
                    if (!savedNadesDict.ContainsKey(playerSteamID))
                    {
                        savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                    }

                    savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                    {
                        { "LineupPos", $"{playerPos.X} {playerPos.Y} {playerPos.Z+4}" },
                        { "LineupAng", $"{playerAngle.X} {playerAngle.Y} {playerAngle.Z}" },
                        { "Desc", lineupDesc },
                        { "Map", currentMapName },
                        { "Type", nadeType }
                    };

                    // Serialize the updated dictionary back to JSON
                    string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                    // Write the updated JSON content back to the file
                    File.WriteAllText(savednadesPath, updatedJson);

                    PrintToPlayerChat(player, Localizer["matchzy.pm.lineupsavedsucces", lineupName]);
                    PrintToAllChat(Localizer["matchzy.pm.playersavedlineup", player.PlayerName, $"{lineupName} {playerPos} {playerAngle}"]);
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .savenade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".savenade <name>"]);
            }
        }

        private void HandleDeleteNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || player == null) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Grab player steamid
                string playerSteamID;
                if(isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    string lineupQuery = saveNadeName.Trim();

                    if (savedNadesDict.TryGetValue(playerSteamID, out Dictionary<string, Dictionary<string, string>>? savedPlayerNades))
                    {
                        List<string> nadeNamesOnCurrentMap = savedPlayerNades
                            .Where(n => n.Value.ContainsKey("Map") && n.Value["Map"] == Server.MapName)
                            .Select(n => n.Key)
                            .ToList();

                        // Resolve the name exactly as .loadnade does, so a full
                        // multi-word query selects the same saved lineup.
                        string nearestName = StringSimilarity.FindNearestName(lineupQuery, nadeNamesOnCurrentMap);

                        if (nadeNamesOnCurrentMap.Contains(nearestName) && savedPlayerNades.ContainsKey(nearestName))
                        {
                            // Remove the specified lineup
                            savedPlayerNades.Remove(nearestName);

                            // Serialize the updated dictionary back to JSON
                            string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                            // Write the updated JSON content back to the file
                            File.WriteAllText(savednadesPath, updatedJson);

                            // ReplyToUserCommand(player, $"Lineup '{nearestName}' deleted successfully.");
                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdeletesuccess", nearestName]);
                            return;
                        }
                    }

                    // ReplyToUserCommand(player, $"Lineup '{lineupQuery}' not found!");
                    ReplyToUserCommand(player, Localizer["matchzy.pm.lineupnotfound", lineupQuery]);
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .delnade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".delnade <name>"]);
            }
        }

        private void HandleImportNadeCommand(CCSPlayerController? player, string saveNadeCode)
        {
            if (!isPractice || player == null) return;

            if (!string.IsNullOrWhiteSpace(saveNadeCode))
            {
                try
                {
                    // Split the code into parts
                    string[] parts = saveNadeCode.Split(' ');

                    // Check if there are enough parts
                    if (parts.Length == 7)
                    {
                        // Extract name, pos, and ang from the parts
                        string lineupName = parts[0].Trim();
                        string[] posAng = parts.Skip(1).Select(p => p.Replace(",", "")).ToArray(); // Replace ',' with '' for proper parsing

                        // Get player info: steamid
                        string playerSteamID = player.SteamID.ToString();
                        string currentMapName = Server.MapName;

                        // Define the file path
                        string savednadesfileName = "MatchZy/savednades.json";
                        string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                        // Read existing JSON content
                        string existingJson = File.ReadAllText(savednadesPath);

                        //Console.WriteLine($"Existing JSON Content: {existingJson}");

                        // Deserialize the existing JSON content
                        var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                            ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                        // Check if the lineup name already exists for the given SteamID on the same map
                        if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                        {
                            var existingLineup = savedNadesDict[playerSteamID][lineupName];
                            if (existingLineup.ContainsKey("Map") && existingLineup["Map"] == currentMapName)
                            {
                                // Lineup already exists on the same map, reply to the user and return
                                // ReplyToUserCommand(player, $"Lineup '{lineupName}' already exists! Please use a different name or use .delnade <nade>");
                                ReplyToUserCommand(player, Localizer["matchzy.pm.lineupalreadyexists", lineupName]);
                                return;
                            }
                        }

                        // Update or add the new lineup information
                        if (!savedNadesDict.ContainsKey(playerSteamID))
                        {
                            savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                        }

                        savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                        {
                            { "LineupPos", $"{posAng[0]} {posAng[1]} {posAng[2]}" },
                            { "LineupAng", $"{posAng[3]} {posAng[4]} {posAng[5]}" },
                            { "Desc", "" },
                            { "Map", currentMapName }
                        };

                        // Serialize the updated dictionary back to JSON
                        string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                        // Write the updated JSON content back to the file
                        File.WriteAllText(savednadesPath, updatedJson);

                        // ReplyToUserCommand(player, $"Lineup '{lineupName}' imported and saved successfully.");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupimportedsuccess"]);
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Invalid code format. Please provide a valid code with name, pos, and ang.");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupinvalidcode"]);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .importnade <code>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".importnade <code>"]);
            }
        }

        private void HandleListNadesCommand(CCSPlayerController? player, string nadeFilter)
        {
            if (!isPractice || player == null) return;

            // Define the file path
            string savednadesfileName = "MatchZy/savednades.json";
            string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

            try
            {
                // Read existing JSON content
                string existingJson = File.ReadAllText(savednadesPath);

                //Console.WriteLine($"Existing JSON Content: {existingJson}");

                // Deserialize the existing JSON content
                var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                    ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                ReplyToUserCommand(player, $"\x0D-----All Saved Lineups for \x06{Server.MapName}\x0D-----");

                // List lineups for the specified player
                ListLineups(player, "default", Server.MapName, savedNadesDict, nadeFilter);

                // List lineups for the current player
                ListLineups(player, player.SteamID.ToString(), Server.MapName, savedNadesDict, nadeFilter);
            }
            catch (JsonException ex)
            {
                Log($"Error handling JSON: {ex.Message}");
                ReplyToUserCommand(player, $"Error handling JSON. Please check the server logs.");
            }
        }

        private void ListLineups(CCSPlayerController player, string steamID, string mapName, Dictionary<string, Dictionary<string, Dictionary<string, string>>> savedNadesDict, string nadeFilter)
        {
            if (savedNadesDict.ContainsKey(steamID))
            {
                foreach (var kvp in savedNadesDict[steamID])
                {
                    // Check if a filter is provided, and if so, apply the filter
                    if ((string.IsNullOrWhiteSpace(nadeFilter) || kvp.Key.Contains(nadeFilter, StringComparison.OrdinalIgnoreCase))
                        && kvp.Value.ContainsKey("Map") && kvp.Value["Map"] == mapName)
                    {
                        // Format and reply with the lineup name
                        ReplyToUserCommand(player, $"\x06[{kvp.Value["Type"]}] \x0D.loadnade \x06{kvp.Key}");
                    }
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"No saved lineups found for the specified SteamID: ({steamID}).");
                ReplyToUserCommand(player, Localizer["matchzy.pm.nosavedlineups", steamID]);

            }
        }

        private void HandleLoadNadeCommand(CCSPlayerController? player, string loadNadeName)
        {
            if (!isPractice || player == null || !IsPlayerValid(player)) return;

            if (!string.IsNullOrWhiteSpace(loadNadeName))
            {
                // Get player info: steamid
                string playerSteamID = player.SteamID.ToString();

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    bool lineupFound = false;
                    bool lineupOnWrongMap = false;

                    // Check for the lineup in the player's steamID and the fixed steamID
                    foreach (string currentSteamID in new[] { playerSteamID, "default" })
                    {
                        if (savedNadesDict.ContainsKey(currentSteamID))
                        {
                            // Filter nade names based on the current map
                            var nadeNamesOnCurrentMap = savedNadesDict[currentSteamID]
                                .Where(n => n.Value.ContainsKey("Map") && n.Value["Map"] == Server.MapName)
                                .Select(n => n.Key)
                                .ToList();

                            // Find the nearest matching name
                            string nearestName = StringSimilarity.FindNearestName(loadNadeName, nadeNamesOnCurrentMap);

                            if (savedNadesDict[currentSteamID].ContainsKey(nearestName))
                            {
                                var lineupInfo = savedNadesDict[currentSteamID][nearestName];

                                // Check if the lineup contains the "Map" key and if it matches the current map
                                if (lineupInfo.ContainsKey("Map") && lineupInfo["Map"] == Server.MapName)
                                {
                                    // Extract position and angle from the lineup information
                                    string[] posArray = lineupInfo["LineupPos"].Split(' ');
                                    string[] angArray = lineupInfo["LineupAng"].Split(' ');

                                    // Parse position and angle
                                    Vector loadedPlayerPos = new Vector(float.Parse(posArray[0]), float.Parse(posArray[1]), float.Parse(posArray[2]));
                                    QAngle loadedPlayerAngle = new QAngle(float.Parse(angArray[0]), float.Parse(angArray[1]), float.Parse(angArray[2]));

                                    // Teleport player
                                    PlayerTeleport.TeleportSafely(player, loadedPlayerPos, loadedPlayerAngle);

                                    // Change player inv slot
                                    switch (lineupInfo["Type"])
                                    {
                                        case "Flash":
                                            player.ExecuteClientCommand("slot7");
                                            break;
                                        case "Smoke":
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                        case "HE":
                                            player.ExecuteClientCommand("slot6");
                                            break;
                                        case "Decoy":
                                            player.ExecuteClientCommand("slot9");
                                            break;
                                        case "Molly":
                                            player.ExecuteClientCommand("slot10");
                                            break;
                                        case "":
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                    }

                                    // Extract description, if available
                                    string lineupDesc = lineupInfo.ContainsKey("Desc") ? lineupInfo["Desc"] : null;

                                    // Print messages
                                    // ReplyToUserCommand(player, $"Lineup {ChatColors.Green}{nearestName}{ChatColors.Default} loaded successfully!");
                                    ReplyToUserCommand(player, Localizer["matchzy.pm.lineuploadedsuccess", nearestName]);

                                    if (!string.IsNullOrWhiteSpace(lineupDesc))
                                    {
                                        player.PrintToCenter($"{lineupDesc}");
                                        // ReplyToUserCommand(player, $"Description: {ChatColors.Green}{lineupDesc}{ChatColors.Default}");
                                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdesc", lineupDesc]);
                                    }

                                    lineupFound = true;
                                    break;
                                }
                                else
                                {
                                    // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{nearestName}{ChatColor.Default} not found on the current map!");
                                    ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfoundonmap", nearestName]);
                                    lineupOnWrongMap = true;
                                }
                            }
                        }
                    }

                    if (!lineupFound && !lineupOnWrongMap)
                    {
                        // Lineup not found
                        // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{loadNadeName}{ChatColor.Default} not found!");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfound", loadNadeName]);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Nade not found! Usage: .loadnade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.pm.loadnadenotfound"]);
            }
        }

        private void CreateSpawnMarkerEdge(Vector start, Vector end, Color color)
        {
            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam == null)
            {
                Log("Failed to create a spawn marker edge");
                return;
            }

            beam.LifeState = 1;
            beam.Width = SpawnMarkerWidth;
            beam.Render = color;

            beam.EndPos.X = end.X;
            beam.EndPos.Y = end.Y;
            beam.EndPos.Z = end.Z;

            beam.Teleport(start, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            beam.DispatchSpawn();
            spawnMarkerBeams.Add(beam);
        }

        public void ShowSpawnMarker(Position spawn, Color color)
        {
            float centerX = spawn.PlayerPosition.X;
            float centerY = spawn.PlayerPosition.Y;
            float markerZ = spawn.PlayerPosition.Z + SpawnMarkerHeightOffset;

            Vector northWest = new(centerX - SpawnMarkerHalfSize, centerY + SpawnMarkerHalfSize, markerZ);
            Vector northEast = new(centerX + SpawnMarkerHalfSize, centerY + SpawnMarkerHalfSize, markerZ);
            Vector southEast = new(centerX + SpawnMarkerHalfSize, centerY - SpawnMarkerHalfSize, markerZ);
            Vector southWest = new(centerX - SpawnMarkerHalfSize, centerY - SpawnMarkerHalfSize, markerZ);

            CreateSpawnMarkerEdge(northWest, northEast, color);
            CreateSpawnMarkerEdge(northEast, southEast, color);
            CreateSpawnMarkerEdge(southEast, southWest, color);
            CreateSpawnMarkerEdge(southWest, northWest, color);
        }

        private void InitializePracticeSpawnMarkers()
        {
            RemoveSpawnMarkerEntities();
            spawnMarkersEnabled = spawnMarkersEnabledByDefault.Value;
        }

        private void RefreshPracticeSpawnMarkersAfterRoundStart()
        {
            if (!isPractice || !spawnMarkersEnabled) return;

            Server.NextFrame(() =>
            {
                if (!isPractice || !spawnMarkersEnabled) return;
                ShowSpawnMarkers();
            });
        }

        private void ShowSpawnMarkers()
        {
            RemoveSpawnMarkerEntities();
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();

            foreach (Position spawn in spawnsData[(byte)CsTeam.CounterTerrorist])
            {
                ShowSpawnMarker(spawn, Color.Blue);
            }

            foreach (Position spawn in spawnsData[(byte)CsTeam.Terrorist])
            {
                ShowSpawnMarker(spawn, Color.Gold);
            }

        }

        private void RemoveSpawnMarkerEntities()
        {
            foreach (CBeam beam in spawnMarkerBeams)
            {
                if (beam.IsValid) beam.Remove();
            }

            spawnMarkerBeams.Clear();
        }

        public void RemoveSpawnMarkers()
        {
            spawnMarkersEnabled = false;
            RemoveSpawnMarkerEntities();
        }

        public void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            HandleConfigurationMenuInput(player, pressed);

            if (!isPractice ||
                !spawnMarkersEnabled ||
                !spawnMarkerBeams.Any(beam => beam.IsValid) ||
                !IsPlayerValid(player)) return;
            if ((pressed & PlayerButtons.Use) == 0) return;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            Vector? playerPosition = pawn?.CBodyComponent?.SceneNode?.AbsOrigin;
            if (playerPosition == null) return;

            Position? selectedSpawn = null;
            float selectedDistanceSquared = float.MaxValue;

            foreach (List<Position> teamSpawns in spawnsData.Values)
            {
                foreach (Position spawn in teamSpawns)
                {
                    float deltaX = playerPosition.X - spawn.PlayerPosition.X;
                    float deltaY = playerPosition.Y - spawn.PlayerPosition.Y;
                    float deltaZ = playerPosition.Z - spawn.PlayerPosition.Z;

                    if (MathF.Abs(deltaX) > SpawnMarkerHalfSize ||
                        MathF.Abs(deltaY) > SpawnMarkerHalfSize ||
                        MathF.Abs(deltaZ) > SpawnMarkerVerticalTolerance)
                    {
                        continue;
                    }

                    float distanceSquared = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
                    if (distanceSquared < selectedDistanceSquared)
                    {
                        selectedDistanceSquared = distanceSquared;
                        selectedSpawn = spawn;
                    }
                }
            }

            if (selectedSpawn != null)
            {
                selectedSpawn.Teleport(player);
            }
        }

        [ConsoleCommand("css_god", "Toggles damage immunity for the requesting human player")]
        public void OnGodCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return;
            ConfigurePracticeHumanGodMode(player, !humanGodModeEnabled.Contains(player.UserId.Value));
        }

        private bool ConfigurePracticeHumanGodMode(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return false;

            int userId = player.UserId.Value;
            if (enabled) humanGodModeEnabled.Add(userId);
            else humanGodModeEnabled.Remove(userId);

            SetPracticeHumanGodMode(player, enabled, restoreHealthTarget: true);
            ReplyToUserCommand(player, "God is " + Localizer[enabled ? "matchzy.cc.enabled" : "matchzy.cc.disabled"]);
            return true;
        }

        [ConsoleCommand("css_prac", "Starts practice mode")]
        [ConsoleCommand("css_tactics", "Starts practice mode")]
        public void OnPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, "Practice Mode cannot be started when a match has been started!");
                ReplyToUserCommand(player, Localizer["matchzy.pm.pracmatchstarted"]);
                return;
            }
	    
			// if (isPractice)
            // {
            //     StartMatchMode();
            //     return;
            // }
	
            StartPracticeMode();
        }

        [ConsoleCommand("css_dry", "Starts dryrun in practice mode")]
        [ConsoleCommand("css_dryrun", "Starts dryrun in practice mode")]
        public void OnDryRunCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (matchStarted)
            {
                // ReplyToUserCommand(player, "Dryrun cannot be started when a match has been started!");
                ReplyToUserCommand(player, Localizer["matchzy.pm.dryrunmatchstarted"]);
                return;
            }
            if (!isPractice)
            {
                // ReplyToUserCommand(player, "Dryrun can only be started in practice mode!");
                ReplyToUserCommand(player, Localizer["matchzy.pm.dryrunnopractice"]);
                return;
            }

            Server.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            noFlashList = new();

            ExecUnpracCommands();
            ExecDryRunCFG();

            isDryRun = true;
        }

        [ConsoleCommand("css_spawn", "Teleport to provided spawn")]
        public void OnSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, player.TeamNum, "spawn");
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: !spawn <round>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!spawn <round>"]);
            }
        }

        [ConsoleCommand("css_ctspawn", "Teleport to provided CT spawn")]
        public void OnCtSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: !ctspawn <round>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!ctspawn <round>"]);
            }
        }

        [ConsoleCommand("css_tspawn", "Teleport to provided T spawn")]
        public void OnTSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player == null || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.Terrorist, "tspawn");
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: !ctspawn <round>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!ctspawn <round>"]);
            }
        }

        [ConsoleCommand("css_bot", "Spawns a bot at the player's position")]
        public void OnBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            AddBot(player, false);
        }

        [ConsoleCommand("css_cbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_crouchbot", "Spawns a crouched bot at the player's position")]
        public void OnCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            AddBot(player, true);
        }

        [ConsoleCommand("css_botshoot", "Controls whether practice bots can shoot enemies")]
        public void OnBotShootCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleBotShootCommand(player, command.ArgByIndex(1));
        }

        private void HandleBotShootCommand(CCSPlayerController? player, string commandArg)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!bool.TryParse(commandArg.Trim(), out bool enabled))
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".botshoot <true/false>"]);
                return;
            }

            SetPracticeBotShooting(player, enabled);
        }

        private bool SetPracticeBotShooting(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player)) return false;

            botShootingEnabled = enabled;
            ResetTurretCombatState();
            ApplyBotShootingState();

            string status = enabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
            ReplyToUserCommand(player, $"Bot shooting is {status}.");
            return true;
        }

        private void ApplyBotShootingState()
        {
            if (botShootingEnabled)
            {
                // CS2 uses custom_bot_difficulty for offline games and bot_difficulty otherwise.
                Server.ExecuteCommand("sv_auto_adjust_bot_difficulty 0");
                Server.ExecuteCommand("bot_difficulty 5");
                Server.ExecuteCommand("custom_bot_difficulty 5");
                Server.ExecuteCommand("bot_stop 0");
                Server.ExecuteCommand("bot_zombie 0");
                // Native firing remains disabled because the plugin applies attack input
                // only after the configured reaction delay has elapsed.
                Server.ExecuteCommand("bot_dont_shoot 1");
                // Keep the combat AI awake so it updates enemy visibility. Per-bot input
                // clearing and position correction below provide the locomotion lock.
                Server.ExecuteCommand("bot_freeze 0");
                return;
            }

            Server.ExecuteCommand("bot_dont_shoot 1");
            Server.ExecuteCommand("bot_stop 1");
            Server.ExecuteCommand("bot_zombie 1");
            Server.ExecuteCommand("bot_freeze 1");
        }

        [ConsoleCommand("css_botreactiontime", "Sets the practice bot reaction time in milliseconds")]
        public void OnBotReactionTimeCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleBotReactionTimeCommand(player, command.ArgByIndex(1));
        }

        private void HandleBotReactionTimeCommand(CCSPlayerController? player, string commandArg)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!int.TryParse(commandArg.Trim(), out int reactionTimeMs) || reactionTimeMs < 0 || reactionTimeMs > 1000)
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".botreactiontime <0-1000>"]);
                return;
            }

            SetPracticeBotReactionTime(player, reactionTimeMs);
        }

        private bool SetPracticeBotReactionTime(CCSPlayerController? player, int reactionTimeMs)
        {
            if (!isPractice || !IsPlayerValid(player) || reactionTimeMs < 0 || reactionTimeMs > 1000) return false;

            botReactionTimeMs = reactionTimeMs;
            ReplyToUserCommand(player, $"Bot reaction time set to {botReactionTimeMs} ms.");
            return true;
        }

        [ConsoleCommand("css_botrespawn", "Controls whether practice bots respawn after death")]
        public void OnBotRespawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleBotRespawnCommand(player, command.ArgByIndex(1));
        }

        private void HandleBotRespawnCommand(CCSPlayerController? player, string commandArg)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!bool.TryParse(commandArg.Trim(), out bool enabled))
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".botrespawn <true/false>"]);
                return;
            }

            SetPracticeBotRespawn(player, enabled);
        }

        private bool SetPracticeBotRespawn(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player)) return false;

            botRespawnEnabled = enabled;
            Server.ExecuteCommand("mp_respawn_on_death_ct 0; mp_respawn_on_death_t 0");
            Server.ExecuteCommand("mp_ignore_round_win_conditions 1");

            if (enabled)
            {
                RespawnDeadPracticeBots();
            }

            string status = enabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
            ReplyToUserCommand(player, $"Bot respawning is {status}.");
            return true;
        }

        [ConsoleCommand("css_botlifereg", "Controls automatic health regeneration for practice bots")]
        public void OnBotLifeRegenerationCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleBotLifeRegenerationCommand(player, command.ArgByIndex(1));
        }

        private void HandleBotLifeRegenerationCommand(CCSPlayerController? player, string commandArg)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!bool.TryParse(commandArg.Trim(), out bool enabled))
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".botlifereg <true/false>"]);
                return;
            }

            SetPracticeBotLifeRegeneration(player, enabled);
        }

        private bool SetPracticeBotLifeRegeneration(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player)) return false;

            botLifeRegenerationEnabled = enabled;
            botHealthCeilings.Clear();
            botNextRegenerationTime = enabled
                ? Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds
                : 0.0f;

            if (enabled)
            {
                HealAllLivingPracticeBots();
            }

            string status = enabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
            ReplyToUserCommand(player, $"Bot health regeneration is {status}.");
            return true;
        }

        [ConsoleCommand("css_liferegon", "Controls automatic health regeneration for the requesting human player")]
        public void OnHumanLifeRegenerationCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleHumanLifeRegenerationCommand(player, command.ArgByIndex(1));
        }

        private void HandleHumanLifeRegenerationCommand(CCSPlayerController? player, string commandArg)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return;

            if (!bool.TryParse(commandArg.Trim(), out bool enabled))
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".liferegon <true/false>"]);
                return;
            }

            SetPracticeHumanLifeRegeneration(player, enabled);
        }

        private bool SetPracticeHumanLifeRegeneration(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return false;

            int userId = player.UserId.Value;
            humanNextRegenerationTimes.Remove(userId);
            DisableEngineHumanHealthProtection();

            if (enabled)
            {
                humanLifeRegenerationEnabled.Add(userId);
                HealPracticeHuman(player);
                humanNextRegenerationTimes[userId] = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
            }
            else
            {
                humanLifeRegenerationEnabled.Remove(userId);
            }

            string status = enabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
            ReplyToUserCommand(player, $"Your health regeneration is {status}.");
            return true;
        }

        private void EnableDefaultHumanLifeRegeneration(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return;

            int userId = player.UserId.Value;
            humanLifeRegenerationEnabled.Add(userId);
            HealPracticeHuman(player);
            humanNextRegenerationTimes[userId] = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
        }

        private void RespawnDeadPracticeBots()
        {
            foreach (Dictionary<string, object> botData in pracUsedBots.Values)
            {
                if (!botData.TryGetValue("controller", out object? controllerValue) ||
                    controllerValue is not CCSPlayerController bot)
                {
                    continue;
                }

                Server.NextFrame(() => RespawnPracticePlayerIfDead(bot, requireBotRespawnEnabled: true));
            }
        }

        private void RespawnPracticePlayerIfDead(CCSPlayerController player, bool requireBotRespawnEnabled)
        {
            if (!isPractice || !IsPlayerValid(player) || player.PawnIsAlive) return;
            if (!player.IsBot && !IsConnectedPracticeHuman(player)) return;
            if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

            if (requireBotRespawnEnabled &&
                (!botRespawnEnabled || !player.IsBot || !player.UserId.HasValue || !pracUsedBots.ContainsKey(player.UserId.Value)))
            {
                return;
            }

            player.Respawn();
        }

        private void SchedulePracticeHumanRespawn(CCSPlayerController? player, float delaySeconds)
        {
            if (!isPractice || player == null || player.IsBot || player.IsHLTV || !IsConnectedPracticeHuman(player)) return;

            AddTimer(
                delaySeconds,
                () => TryRespawnJoinedPracticeHuman(player, attemptsRemaining: 5),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void SchedulePracticeSideInventoryUpdate(
            CCSPlayerController? player,
            CsTeam previousTeam,
            CsTeam newTeam)
        {
            bool replacesMolotov = previousTeam == CsTeam.Terrorist && newTeam == CsTeam.CounterTerrorist;
            bool removesDefuseKit = previousTeam == CsTeam.CounterTerrorist && newTeam == CsTeam.Terrorist;
            if ((!replacesMolotov && !removesDefuseKit) ||
                player == null ||
                player.IsBot ||
                player.IsHLTV ||
                !IsConnectedPracticeHuman(player))
            {
                return;
            }

            AddTimer(
                PracticeSideInventoryUpdateDelaySeconds,
                () => TryApplyPracticeSideInventoryUpdate(player, previousTeam, newTeam, attemptsRemaining: 5),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void TryApplyPracticeSideInventoryUpdate(
            CCSPlayerController player,
            CsTeam previousTeam,
            CsTeam newTeam,
            int attemptsRemaining)
        {
            if (!isPractice ||
                player.IsBot ||
                player.IsHLTV ||
                !IsConnectedPracticeHuman(player) ||
                player.Team != newTeam)
            {
                return;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (!IsPlayerValid(player) || !player.PawnIsAlive || pawn == null || !pawn.IsValid)
            {
                if (attemptsRemaining <= 0) return;
                AddTimer(
                    0.2f,
                    () => TryApplyPracticeSideInventoryUpdate(player, previousTeam, newTeam, attemptsRemaining - 1),
                    TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }

            if (previousTeam == CsTeam.Terrorist && newTeam == CsTeam.CounterTerrorist)
            {
                ReplacePracticeMolotovWithIncendiary(player, pawn);
            }
            else if (previousTeam == CsTeam.CounterTerrorist && newTeam == CsTeam.Terrorist)
            {
                CCSPlayer_ItemServices? itemServices = pawn.ItemServices?.As<CCSPlayer_ItemServices>();
                if (itemServices != null && itemServices.HasDefuser)
                {
                    itemServices.HasDefuser = false;
                    Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
                }
            }
        }

        private static void ReplacePracticeMolotovWithIncendiary(
            CCSPlayerController player,
            CCSPlayerPawn pawn)
        {
            if (pawn.WeaponServices == null) return;

            List<CBasePlayerWeapon> molotovs = pawn.WeaponServices.MyWeapons
                .Where(weapon => weapon.IsValid &&
                    weapon.Value != null &&
                    weapon.Value.IsValid &&
                    weapon.Value.DesignerName.Equals("weapon_molotov", StringComparison.OrdinalIgnoreCase))
                .Select(weapon => weapon.Value!)
                .ToList();
            if (molotovs.Count == 0) return;

            bool hasIncendiary = pawn.WeaponServices.MyWeapons.Any(weapon =>
                weapon.IsValid &&
                weapon.Value != null &&
                weapon.Value.IsValid &&
                weapon.Value.DesignerName.Equals("weapon_incgrenade", StringComparison.OrdinalIgnoreCase));

            foreach (CBasePlayerWeapon molotov in molotovs)
            {
                pawn.RemovePlayerItem(molotov);
                if (molotov.IsValid) molotov.Remove();
            }

            if (!hasIncendiary)
            {
                player.GiveNamedItem("weapon_incgrenade");
            }
        }

        private void TryRespawnJoinedPracticeHuman(CCSPlayerController player, int attemptsRemaining)
        {
            if (!isPractice || player.IsBot || player.IsHLTV || !IsConnectedPracticeHuman(player) || player.PawnIsAlive) return;
            if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

            if (IsPlayerValid(player))
            {
                RespawnPracticePlayerIfDead(player, requireBotRespawnEnabled: false);
                return;
            }

            if (attemptsRemaining <= 0) return;
            AddTimer(
                0.2f,
                () => TryRespawnJoinedPracticeHuman(player, attemptsRemaining - 1),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private bool IsConnectedPracticeHuman(CCSPlayerController player)
        {
            if (!player.IsValid ||
                player.Connected != PlayerConnectedState.Connected ||
                !player.UserId.HasValue)
            {
                return false;
            }

            return playerData.TryGetValue(player.UserId.Value, out CCSPlayerController? connectedPlayer) &&
                connectedPlayer.Handle == player.Handle;
        }

        private void CaptureDisconnectingPracticeHumanPawn(int playerSlot)
        {
            if (!isPractice) return;

            CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || !player.PlayerPawn.IsValid) return;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            disconnectingPracticeHumanPawnHandles[playerSlot] = pawn.EntityHandle.Raw;
        }

        private void RemoveDisconnectedPracticeHumanPawn(int playerSlot)
        {
            if (!disconnectingPracticeHumanPawnHandles.Remove(playerSlot, out uint pawnHandleRaw)) return;

            // Wait until CS2 has completed its normal disconnect processing so C4,
            // weapons, and other carried items can be dropped before the pawn goes away.
            Server.NextFrame(() =>
            {
                CEntityHandle pawnHandle = new(pawnHandleRaw);
                if (!pawnHandle.IsValid) return;

                CEntityInstance? pawnEntity = pawnHandle.Value;
                if (pawnEntity == null || !pawnEntity.IsValid || pawnEntity.EntityHandle.Raw != pawnHandleRaw) return;

                pawnEntity.Remove();
            });
        }

        private void LockShootingBotsInPlace()
        {
            MaintainPracticeRoundTimeout();
            MaintainBotLifeRegeneration();
            MaintainPracticeHumanGodModes();
            MaintainHumanLifeRegeneration();
            MaintainHumanPracticeArmor();

            if (!isPractice || !botShootingEnabled || pracUsedBots.Count == 0) return;

            foreach (Dictionary<string, object> botData in pracUsedBots.Values)
            {
                if (!botData.TryGetValue("controller", out object? controllerValue) ||
                    controllerValue is not CCSPlayerController bot ||
                    !IsPlayerValid(bot) ||
                    !bot.PawnIsAlive ||
                    !botData.TryGetValue("position", out object? positionValue) ||
                    positionValue is not Position lockedPosition)
                {
                    continue;
                }

                CCSPlayerPawn? pawn = bot.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                CCSBot? botState = pawn.Bot;
                if (botState == null) continue;

                // Clear navigation input as well as correcting the physical position. This
                // prevents the running animation and diagonal movement attempt from surviving
                // even when the bot AI produces another pathfinding decision.
                botState.ForwardSpeed = 0.0f;
                botState.LeftSpeed = 0.0f;
                botState.VerticalSpeed = 0.0f;
                botState.ButtonFlags &= ~((ulong)PlayerButtons.Forward |
                    (ulong)PlayerButtons.Back |
                    (ulong)PlayerButtons.Left |
                    (ulong)PlayerButtons.Right |
                    (ulong)PlayerButtons.Moveleft |
                    (ulong)PlayerButtons.Moveright |
                    (ulong)PlayerButtons.Jump);

                CCSPlayerPawn? targetPawn = FindTurretTarget(bot, botState);
                if (targetPawn == null)
                {
                    ResetTurretReaction(bot, botState);
                    LockBotPosition(pawn, lockedPosition, null);
                    continue;
                }

                uint targetHandle = targetPawn.EntityHandle.Raw;
                bool targetWasCurrentEnemy = botState.Enemy.Raw == targetHandle;
                botState.Enemy.Raw = targetHandle;

                Vector? targetOrigin = targetPawn.AbsOrigin;
                if (targetOrigin == null)
                {
                    ResetTurretReaction(bot, botState);
                    continue;
                }
                float targetX = targetOrigin.X + targetPawn.ViewOffset.X;
                float targetY = targetOrigin.Y + targetPawn.ViewOffset.Y;
                float targetZ = targetOrigin.Z + targetPawn.ViewOffset.Z;
                Vector botEye = botState.EyePosition;

                float deltaX = targetX - botEye.X;
                float deltaY = targetY - botEye.Y;
                float deltaZ = targetZ - botEye.Z;
                float horizontalDistance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                float pitch = -MathF.Atan2(deltaZ, horizontalDistance) * 180.0f / MathF.PI;
                float yaw = MathF.Atan2(deltaY, deltaX) * 180.0f / MathF.PI;
                QAngle aimAngle = new(pitch, yaw, 0.0f);

                LockBotPosition(pawn, lockedPosition, aimAngle);
                bot.ExecuteClientCommandFromServer(string.Create(
                    CultureInfo.InvariantCulture,
                    $"setang {pitch:R} {yaw:R} 0"));

                // IsEnemyVisible is calculated by the game's own bot vision. Losing sight
                // or changing target starts a new reaction delay before firing is permitted.
                UpdateTurretReaction(bot, botState, targetHandle, targetWasCurrentEnemy && botState.IsEnemyVisible);
            }
        }

        private void MaintainHumanPracticeArmor()
        {
            if (!isPractice) return;

            foreach (CCSPlayerController player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player) || player.IsBot || player.IsHLTV || !player.PawnIsAlive ||
                    (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist))
                {
                    continue;
                }

                CCSPlayerPawn? pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.ArmorValue >= 100) continue;

                pawn.ArmorValue = 100;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            }
        }

        private void UpdateTurretReaction(CCSPlayerController bot, CCSBot botState, uint targetHandle, bool targetVisible)
        {
            if (!bot.UserId.HasValue) return;

            int userId = bot.UserId.Value;
            if (!targetVisible)
            {
                ResetTurretReaction(bot, botState);
                return;
            }

            if (!botReactionStates.TryGetValue(userId, out (uint TargetHandle, float VisibleSince) reactionState) ||
                reactionState.TargetHandle != targetHandle)
            {
                reactionState = (targetHandle, Server.CurrentTime);
                botReactionStates[userId] = reactionState;
                SetTurretBotAttacking(bot, botState, botReactionTimeMs == 0);
                return;
            }

            float requiredDelaySeconds = botReactionTimeMs / 1000.0f;
            bool reactionDelayElapsed = Server.CurrentTime - reactionState.VisibleSince >= requiredDelaySeconds;
            SetTurretBotAttacking(bot, botState, reactionDelayElapsed);
        }

        private void ResetTurretReaction(CCSPlayerController bot, CCSBot botState)
        {
            if (bot.UserId.HasValue)
            {
                botReactionStates.Remove(bot.UserId.Value);
            }
            SetTurretBotAttacking(bot, botState, false);
        }

        private void SetTurretBotAttacking(CCSPlayerController bot, CCSBot botState, bool attacking)
        {
            if (!bot.UserId.HasValue) return;

            int userId = bot.UserId.Value;
            if (attacking)
            {
                botState.ButtonFlags |= (ulong)PlayerButtons.Attack;
                if (turretBotsAttacking.Add(userId))
                {
                    bot.ExecuteClientCommandFromServer("+attack");
                }
                return;
            }

            botState.ButtonFlags &= ~(ulong)PlayerButtons.Attack;
            if (turretBotsAttacking.Remove(userId))
            {
                bot.ExecuteClientCommandFromServer("-attack");
            }
        }

        private void ResetTurretCombatState()
        {
            foreach (Dictionary<string, object> botData in pracUsedBots.Values)
            {
                if (!botData.TryGetValue("controller", out object? controllerValue) ||
                    controllerValue is not CCSPlayerController bot ||
                    !IsPlayerValid(bot))
                {
                    continue;
                }

                CCSBot? botState = bot.PlayerPawn.Value?.Bot;
                if (botState != null)
                {
                    botState.ButtonFlags &= ~(ulong)PlayerButtons.Attack;
                }
                bot.ExecuteClientCommandFromServer("-attack");
            }

            botReactionStates.Clear();
            turretBotsAttacking.Clear();
        }

        private void RecordPracticeBotDamage(CCSPlayerController bot, int postDamageHealth)
        {
            if (!bot.UserId.HasValue || !pracUsedBots.ContainsKey(bot.UserId.Value))
            {
                return;
            }

            int userId = bot.UserId.Value;
            if (botLifeRegenerationEnabled)
            {
                botHealthCeilings.Remove(userId);
            }
            else
            {
                // Remember the actual post-damage HP so a global regeneration cvar cannot
                // raise this bot's health again on a later tick.
                botHealthCeilings[userId] = postDamageHealth;
            }
        }

        private void MaintainBotLifeRegeneration()
        {
            if (!isPractice) return;
            if (!botLifeRegenerationEnabled)
            {
                SuppressPracticeBotHealthRegeneration();
                return;
            }
            if (Server.CurrentTime < botNextRegenerationTime) return;

            botNextRegenerationTime = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
            HealAllLivingPracticeBots();
        }

        private void MaintainHumanLifeRegeneration()
        {
            if (!isPractice) return;

            foreach (int userId in humanLifeRegenerationEnabled.ToList())
            {
                if (!humanNextRegenerationTimes.TryGetValue(userId, out float nextRegenerationTime))
                {
                    humanNextRegenerationTimes[userId] = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
                    continue;
                }
                if (Server.CurrentTime < nextRegenerationTime) continue;

                humanNextRegenerationTimes[userId] = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
                CCSPlayerController? player = Utilities.GetPlayers()
                    .FirstOrDefault(candidate => candidate.UserId == userId);
                if (!IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.PawnIsAlive)
                {
                    continue;
                }

                HealPracticeHuman(player);
            }
        }

        private static void DisableEngineHumanHealthProtection()
        {
            ConVar.Find("buddha")?.SetValue(false);
            ConVar.Find("sv_regeneration_force_on")?.SetValue(false);
        }

        private void MaintainPracticeHumanGodModes()
        {
            if (!isPractice || humanGodModeEnabled.Count == 0) return;

            foreach (int userId in humanGodModeEnabled.ToList())
            {
                CCSPlayerController? player = Utilities.GetPlayers()
                    .FirstOrDefault(candidate => candidate.UserId == userId);
                if (!IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.PawnIsAlive) continue;

                SetPracticeHumanGodMode(player, enabled: true, restoreHealthTarget: false);
            }
        }

        private static void SetPracticeHumanGodMode(
            CCSPlayerController player,
            bool enabled,
            bool restoreHealthTarget)
        {
            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            bool takesDamage = !enabled;
            if (pawn.TakesDamage != takesDamage)
            {
                pawn.TakesDamage = takesDamage;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_bTakesDamage");
            }

            if (restoreHealthTarget && pawn.Health > 0)
            {
                int healthTarget = enabled ? GodModeHealth : GetNormalPracticeHumanHealthTarget(pawn);
                SetPracticePlayerHealth(pawn, healthTarget);
            }
        }

        private void ResetPracticeHumanGodModes()
        {
            foreach (CCSPlayerController player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player) || player.IsBot || player.IsHLTV) continue;
                SetPracticeHumanGodMode(player, enabled: false, restoreHealthTarget: true);
            }
            humanGodModeEnabled.Clear();
        }

        private static int GetNormalPracticeHumanHealthTarget(CCSPlayerPawn pawn)
        {
            return Math.Max(pawn.MaxHealth, 100);
        }

        private void HealPracticeHuman(CCSPlayerController player)
        {
            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0) return;

            bool godModeEnabled = player.UserId.HasValue && humanGodModeEnabled.Contains(player.UserId.Value);
            int healthTarget = godModeEnabled ? GodModeHealth : GetNormalPracticeHumanHealthTarget(pawn);
            if (pawn.Health >= healthTarget) return;

            SetPracticePlayerHealth(pawn, healthTarget);
        }

        private void SuppressPracticeBotHealthRegeneration()
        {
            foreach (KeyValuePair<int, Dictionary<string, object>> trackedBot in pracUsedBots)
            {
                if (!trackedBot.Value.TryGetValue("controller", out object? controllerValue) ||
                    controllerValue is not CCSPlayerController bot ||
                    !IsPlayerValid(bot) ||
                    !bot.PawnIsAlive)
                {
                    continue;
                }

                CCSPlayerPawn? pawn = bot.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.Health <= 0) continue;

                int currentHealth = pawn.Health;
                if (!botHealthCeilings.TryGetValue(trackedBot.Key, out int healthCeiling))
                {
                    botHealthCeilings[trackedBot.Key] = currentHealth;
                    continue;
                }

                if (currentHealth < healthCeiling)
                {
                    botHealthCeilings[trackedBot.Key] = currentHealth;
                }
                else if (currentHealth > healthCeiling)
                {
                    SetPracticePlayerHealth(pawn, healthCeiling);
                }
            }
        }

        private void HealAllLivingPracticeBots()
        {
            foreach (Dictionary<string, object> botData in pracUsedBots.Values)
            {
                if (botData.TryGetValue("controller", out object? controllerValue) &&
                    controllerValue is CCSPlayerController bot &&
                    IsPlayerValid(bot) &&
                    bot.PawnIsAlive)
                {
                    HealPracticeBot(bot);
                }
            }
        }

        private static void HealPracticeBot(CCSPlayerController bot)
        {
            CCSPlayerPawn? pawn = bot.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0 || pawn.Health >= 100) return;

            SetPracticePlayerHealth(pawn, 100);
        }

        private static void SetPracticePlayerHealth(CCSPlayerPawn pawn, int health)
        {
            pawn.Health = health;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        private CCSPlayerPawn? FindTurretTarget(CCSPlayerController bot, CCSBot botState)
        {
            CCSPlayerPawn? currentEnemy = botState.Enemy.Value;
            if (IsValidTurretTarget(bot, currentEnemy)) return currentEnemy;

            CCSPlayerPawn? botPawn = bot.PlayerPawn.Value;
            if (botPawn == null) return null;

            Vector? botOrigin = botPawn.AbsOrigin;
            if (botOrigin == null) return null;
            CCSPlayerPawn? closestTarget = null;
            float closestDistanceSquared = float.MaxValue;

            foreach (CCSPlayerController candidate in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
            {
                CCSPlayerPawn? candidatePawn = candidate.PlayerPawn.Value;
                if (!IsPlayerValid(candidate) || candidate.IsHLTV || !candidate.PawnIsAlive ||
                    candidate.TeamNum == bot.TeamNum || !IsValidTurretTarget(bot, candidatePawn))
                {
                    continue;
                }

                Vector? candidateOrigin = candidatePawn!.AbsOrigin;
                if (candidateOrigin == null) continue;
                float deltaX = candidateOrigin.X - botOrigin.X;
                float deltaY = candidateOrigin.Y - botOrigin.Y;
                float deltaZ = candidateOrigin.Z - botOrigin.Z;
                float distanceSquared = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
                if (distanceSquared >= closestDistanceSquared) continue;

                closestDistanceSquared = distanceSquared;
                closestTarget = candidatePawn;
            }

            return closestTarget;
        }

        private static bool IsValidTurretTarget(CCSPlayerController bot, CCSPlayerPawn? targetPawn)
        {
            if (targetPawn == null || !targetPawn.IsValid || targetPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                return false;
            }

            byte targetTeam = targetPawn.TeamNum;
            return targetTeam != bot.TeamNum &&
                (targetTeam == (byte)CsTeam.Terrorist || targetTeam == (byte)CsTeam.CounterTerrorist);
        }

        private static void LockBotPosition(CCSPlayerPawn pawn, Position lockedPosition, QAngle? aimAngle)
        {
            System.Numerics.Vector3? teleportAngle = aimAngle == null
                ? null
                : new System.Numerics.Vector3(0.0f, aimAngle.Y, 0.0f);

            pawn.Teleport(
                new System.Numerics.Vector3(
                    lockedPosition.PlayerPosition.X,
                    lockedPosition.PlayerPosition.Y,
                    lockedPosition.PlayerPosition.Z),
                teleportAngle,
                System.Numerics.Vector3.Zero);
        }

        private void MaintainPracticeRoundTimeout()
        {
            if (!isPractice || practiceRoundTimeoutEnding) return;

            CCSGameRulesProxy? gameRulesProxy = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault();
            CCSGameRules? gameRules = gameRulesProxy?.GameRules;
            if (gameRules == null || gameRules.FreezePeriod || gameRules.RoundWinStatus != 0)
            {
                return;
            }

            float roundEndTime = gameRules.RoundStartTime + gameRules.RoundTime;
            if (gameRules.RoundTime <= 0 || Server.CurrentTime < roundEndTime) return;

            practiceRoundTimeoutEnding = true;
            Server.ExecuteCommand("mp_ignore_round_win_conditions 0");

            RoundEndReason timeoutReason = gameRules.MapHasBombTarget || gameRules.MapHasBombZone
                ? RoundEndReason.TargetSaved
                : gameRules.MapHasRescueZone
                    ? RoundEndReason.HostagesNotRescued
                    : RoundEndReason.RoundDraw;
            gameRules.TerminateRound(0.1f, timeoutReason);
        }

        private void ResetPracticeRoundTimeout()
        {
            practiceRoundTimeoutEnding = false;
            if (isPractice)
            {
                DisablePracticeTeamDamagePenalties();
                Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
            }
        }

        [ConsoleCommand("css_boost", "Spawns a bot at the player's position and boost the player on it")]
        public void OnBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            AddBot(player, false);
            AddTimer(0.2f, () => ElevatePlayer(player));
        }

        [ConsoleCommand("css_crouchboost", "Spawns a crouched bot at the player's position and boost the player on it")]
        public void OnCrouchBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            AddBot(player, true);
            AddTimer(0.2f, () => ElevatePlayer(player));
        }

        private void AddBot(CCSPlayerController? player, bool crouch)
        {
            try
            {
                if (!isPractice || player == null || !player.IsValid || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null) return;
                CCSPlayer_MovementServices movementService = new(player.PlayerPawn.Value.MovementServices!.Handle);

                if ((int)movementService.DuckAmount == 1)
                {
                    // Player was crouching while using .bot command
                    crouch = true;
                }
                isSpawningBot = true;
                ApplyBotShootingState();
                // !bot/.bot command is made using a lot of workarounds, as there is no direct way to create a bot entity and spawn it in CSSharp
                // Hence there can be some issues with this approach. This will be revamped when we will be able to fake clients.
                if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
                {
                    Server.ExecuteCommand("bot_join_team T");
                    Server.ExecuteCommand("bot_add_t");
                }
                else if (player.TeamNum == (byte)CsTeam.Terrorist)
                {
                    Server.ExecuteCommand("bot_join_team CT");
                    Server.ExecuteCommand("bot_add_ct");
                }
                
                // Once bot is added, we teleport it to the requested position
                AddTimer(0.1f, () => SpawnBot(player, crouch));
            }
            catch (JsonException ex)
            {
                Log($"[AddBot - FATAL] Error: {ex.Message}");
            }
        }

        private void SpawnBot(CCSPlayerController botOwner, bool crouch)
        {
            try 
            {
                if (!IsPlayerValid(botOwner)) return;
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                bool unusedBotFound = false;
                foreach (var tempPlayer in playerEntities)
                {
                    if (!IsPlayerValid(tempPlayer)) continue;
                    if (!tempPlayer.IsBot || tempPlayer.IsHLTV) continue;
                    if (tempPlayer.UserId.HasValue)
                    {
                        if (!pracUsedBots.ContainsKey(tempPlayer.UserId.Value) && unusedBotFound)
                        {
                            Log($"UNUSED BOT FOUND: {tempPlayer.UserId.Value} EXECUTING: kickid {tempPlayer.UserId.Value}");
                            // Kicking the unused bot. We have to do this because bot_add_t/bot_add_ct may add multiple bots but we need only 1, so we kick the remaining unused ones
                            Server.ExecuteCommand($"kickid {tempPlayer.UserId.Value}");
                            continue;
                        }
                        if (pracUsedBots.ContainsKey(tempPlayer.UserId.Value))
                        {
                            continue;
                        }
                        pracUsedBots[tempPlayer.UserId.Value] = new Dictionary<string, object>();

                        Position botOwnerPosition = new Position(botOwner.PlayerPawn.Value!.CBodyComponent?.SceneNode?.AbsOrigin!, botOwner.PlayerPawn.Value!.CBodyComponent?.SceneNode?.AbsRotation!);
                        // Add key-value pairs to the inner dictionary
                        pracUsedBots[tempPlayer.UserId.Value]["controller"] = tempPlayer;
                        pracUsedBots[tempPlayer.UserId.Value]["position"] = botOwnerPosition;
                        pracUsedBots[tempPlayer.UserId.Value]["owner"] = botOwner;
                        pracUsedBots[tempPlayer.UserId.Value]["crouchstate"] = crouch;

                        // Newly added bot controllers can join dead because practice mode
                        // disables the engine's team-wide respawn cvars. Give every new bot
                        // one initial spawn regardless of whether later death respawns are enabled.
                        if (!tempPlayer.PawnIsAlive)
                        {
                            tempPlayer.Respawn();
                        }

                        if (crouch)
                        {
                            CCSPlayer_MovementServices movementService = new(tempPlayer.PlayerPawn.Value!.MovementServices!.Handle);
                            AddTimer(0.1f, () => movementService.DuckAmount = 1);
                            AddTimer(0.2f, () => tempPlayer.PlayerPawn.Value!.Bot!.IsCrouching = true);
                        }

                        tempPlayer.PlayerPawn.Value!.Teleport(botOwnerPosition.PlayerPosition, botOwnerPosition.PlayerAngle, new Vector(0, 0, 0));
                        TemporarilyDisableCollisions(botOwner, tempPlayer);
                        unusedBotFound = true;
                    }
                }
                if (!unusedBotFound) {
                    // Server.PrintToChatAll($"{chatPrefix} Cannot add bots, the team is full! Use .nobots to remove the current bots.");
                    PrintToAllChat(Localizer["matchzy.pm.botlimit"]);
                }

                isSpawningBot = false;
            }
            catch (JsonException ex)
            {
                Log($"[SpawnBot - FATAL] Error: {ex.Message}");
            }
        }

        public void TemporarilyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            Log($"[TemporarilyDisableCollisions] Disabling {p1.PlayerName} {p2.PlayerName}");
            // Reference collision code: https://github.com/Source2ZE/CS2Fixes/blob/f009e399ff23a81915e5a2b2afda20da2ba93ada/src/events.cpp#L150
            p1.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p1.PlayerPawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2.PlayerPawn.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            // TODO: call CollisionRulesChanged
            var p1p = p1.PlayerPawn;
            var p2p = p2.PlayerPawn;
            collisionGroupTimer?.Kill();
            collisionGroupTimer = AddTimer(0.1f, () =>
            {
                if (!p1p.IsValid || !p2p.IsValid || !p1p.Value.IsValid || !p2p.Value.IsValid)
                {
                    Log($"player handle invalid p1p {p1p.Value.IsValid} p2p {p2p.Value.IsValid}");
                    collisionGroupTimer?.Kill();
                    return;
                }

                if (!DoPlayersCollide(p1p.Value, p2p.Value))
                {
                    // Once they no longer collide 
                    p1p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    p1p.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    p2p.Value.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    p2p.Value.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    // TODO: call CollisionRulesChanged
                    collisionGroupTimer?.Kill();
                }

            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public bool DoPlayersCollide(CCSPlayerPawn p1, CCSPlayerPawn p2)
        {
            Vector p1min, p1max, p2min, p2max;
            var p1pos = p1.AbsOrigin;
            var p2pos = p2.AbsOrigin;
            p1min = p1.Collision.Mins + p1pos!;
            p1max = p1.Collision.Maxs + p1pos!;
            p2min = p2.Collision.Mins + p2pos!;
            p2max = p2.Collision.Maxs + p2pos!;

            return p1min.X <= p2max.X && p1max.X >= p2min.X &&
                    p1min.Y <= p2max.Y && p1max.Y >= p2min.Y &&
                    p1min.Z <= p2max.Z && p1max.Z >= p2min.Z;
        }

        private static void ElevatePlayer(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null) return;
            PlayerTeleport.TeleportSafely(
                player,
                new Vector(
                    player.PlayerPawn.Value.CBodyComponent!.SceneNode!.AbsOrigin.X,
                    player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin.Y,
                    player.PlayerPawn.Value.CBodyComponent.SceneNode.AbsOrigin.Z + 80.0f),
                player.PlayerPawn.Value.EyeAngles);
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;

            // disable noclip on spawn -- all no clipping functionality is handled by the plugin!
            // Movement adjustments are consistent with cs2-noclip.
            CBasePlayerPawn pawn = player!.PlayerPawn.Value!;
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }

            if (matchStarted && (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!)))
            {
                player!.InGameMoneyServices!.Account = 0;

                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                
                return HookResult.Continue;
            }

            // Respawing a bot where it was actually spawned during practice session
            if (isPractice && player!.IsValid && player.IsBot && player.UserId.HasValue)
            {
                if (pracUsedBots.ContainsKey(player.UserId.Value))
                {
                    if (pracUsedBots[player.UserId.Value]["position"] is Position botPosition)
                    {
                        player.PlayerPawn.Value?.Teleport(botPosition.PlayerPosition, botPosition.PlayerAngle, new Vector(0, 0, 0));
                        bool isCrouched = (bool)pracUsedBots[player.UserId.Value]["crouchstate"];
                        if (isCrouched)
                        {
                            player.PlayerPawn.Value!.Flags |= (uint)PlayerFlags.FL_DUCKING;
                            CCSPlayer_MovementServices movementService = new(player.PlayerPawn.Value.MovementServices!.Handle);
                            AddTimer(0.1f, () => movementService.DuckAmount = 1);
                            AddTimer(0.2f, () => player.PlayerPawn.Value.Bot!.IsCrouching = true);
                        }
                        CCSPlayerController? botOwner = (CCSPlayerController)pracUsedBots[player.UserId.Value]["owner"];
                        if (botOwner != null && botOwner.IsValid && botOwner.PlayerPawn != null && botOwner.PlayerPawn.IsValid) {
                            AddTimer(0.2f, () => TemporarilyDisableCollisions(botOwner, player));
                        } 
                    }
                }
                else if (!isSpawningBot && !player.IsHLTV)
                {
                    // Bot has been spawned, but we didn't spawn it, so kick it.
                    // This most often happens when a player changes team with bot_quota_mode set to fill
                    // Extra bots from bot_add are already handled in SpawnBot
                    // Delay this for a few seconds to prevent crashes
                    Log($"Kicking bot {player.PlayerName} due to erroneous spawning");
                    AddTimer(2.5f, () =>
                    {
                        Server.ExecuteCommand($"bot_kick {player.PlayerName}");
                    });
                }
            }
            else if (isPractice && !player.IsBot && !player.IsHLTV && player.UserId.HasValue &&
                (player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist))
            {
                int userId = player.UserId.Value;
                humanNextRegenerationTimes.Remove(userId);
                if (humanLifeRegenerationEnabled.Contains(userId))
                {
                    humanNextRegenerationTimes[userId] = Server.CurrentTime + PracticeLifeRegenerationIntervalSeconds;
                }
                bool godModeEnabled = humanGodModeEnabled.Contains(userId);
                SetPracticeHumanGodMode(
                    player,
                    godModeEnabled,
                    restoreHealthTarget: godModeEnabled);

                if (savedPlayerLocationData.TryGetValue(userId, out PlayerLocationData? savedLocation))
                {
                    savedLocation.LoadPosition(player);
                }
                else if (spawnsData.TryGetValue(player.TeamNum, out List<Position>? teamSpawns) && teamSpawns.Count > 0)
                {
                    teamSpawns[Random.Shared.Next(teamSpawns.Count)].Teleport(player);
                }
            }

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPracticePlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            if (!isPractice) return HookResult.Continue;

            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;

            bool isTrackedPracticeBot = player!.IsBot &&
                player.UserId.HasValue &&
                pracUsedBots.ContainsKey(player.UserId.Value);
            bool requireBotRespawnEnabled = player.IsBot;

            if (player.UserId.HasValue)
            {
                int userId = player.UserId.Value;
                botHealthCeilings.Remove(userId);
                humanNextRegenerationTimes.Remove(userId);
                botReactionStates.Remove(userId);
                turretBotsAttacking.Remove(userId);
                CCSBot? deadBotState = player.PlayerPawn.Value?.Bot;
                if (deadBotState != null)
                {
                    deadBotState.ButtonFlags &= ~(ulong)PlayerButtons.Attack;
                }
                player.ExecuteClientCommandFromServer("-attack");
            }

            if (!player.IsBot || (isTrackedPracticeBot && botRespawnEnabled))
            {
                AddTimer(
                    PracticeRespawnDelaySeconds,
                    () => RespawnPracticePlayerIfDead(player, requireBotRespawnEnabled),
                    TimerFlags.STOP_ON_MAPCHANGE);
            }

            return HookResult.Continue;
        }

        [ConsoleCommand("css_nobots", "Removes bots from the practice session")]
        public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null) return;
            RemoveAllPracticeBots();
        }

        [ConsoleCommand("css_sbp", "Saves all bot positions under the provided name")]
        public void OnSaveBotPositionsCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleSaveBotPositionsCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_lbp", "Loads all bot positions saved under the provided name")]
        public void OnLoadBotPositionsCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleLoadBotPositionsCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_dbp", "Deletes the bot positions saved under the provided name")]
        public void OnDeleteBotPositionsCommand(CCSPlayerController? player, CommandInfo command)
        {
            HandleDeleteBotPositionsCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_listbp", "Lists bot positions saved for the current map")]
        public void OnListBotPositionsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!TryGetSavedBotPositionPresetNames(player!, out List<string> presetNames))
            {
                ReplyToUserCommand(player, "Unable to list the saved bot positions.");
                return;
            }

            if (presetNames.Count == 0)
            {
                ReplyToUserCommand(player, $"No bot-position presets are saved for {Server.MapName}.");
                return;
            }

            ReplyToUserCommand(player, $"Bot-position presets saved for {Server.MapName}:");
            foreach (string presetName in presetNames)
            {
                player!.PrintToChat($" {ChatColors.Green}- {ChatColors.Default}{presetName}");
            }
        }

        private void HandleSaveBotPositionsCommand(CCSPlayerController? player, string rawPresetName)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            string presetName = NormalizeBotPresetName(rawPresetName);
            if (string.IsNullOrWhiteSpace(presetName))
            {
                ReplyToUserCommand(player, "Usage: .sbp <name>");
                return;
            }

            List<SavedBotPosition> savedBots = new();
            foreach (CCSPlayerController bot in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
            {
                if (!IsPlayerValid(bot) || !bot.IsBot || bot.IsHLTV || bot.PlayerPawn.Value == null) continue;
                if (bot.TeamNum != (byte)CsTeam.Terrorist && bot.TeamNum != (byte)CsTeam.CounterTerrorist) continue;

                CCSPlayerPawn pawn = bot.PlayerPawn.Value;
                Vector? origin = pawn.CBodyComponent?.SceneNode?.AbsOrigin;
                if (origin == null) continue;

                QAngle viewAngle = pawn.EyeAngles;
                bool crouched = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0;
                if (pawn.MovementServices != null)
                {
                    CCSPlayer_MovementServices movementServices = new(pawn.MovementServices.Handle);
                    crouched = crouched || movementServices.DuckAmount > 0.5f;
                }

                savedBots.Add(new SavedBotPosition
                {
                    TeamNum = bot.TeamNum,
                    PositionX = origin.X,
                    PositionY = origin.Y,
                    PositionZ = origin.Z,
                    ViewPitch = viewAngle.X,
                    ViewYaw = viewAngle.Y,
                    ViewRoll = viewAngle.Z,
                    Crouched = crouched
                });
            }

            if (savedBots.Count == 0)
            {
                ReplyToUserCommand(player, "No bots are available to save.");
                return;
            }

            try
            {
                string presetsPath = GetSavedBotPositionsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(presetsPath)!);
                Dictionary<string, Dictionary<string, SavedBotPositionPreset>> presets = ReadSavedBotPositionPresets(presetsPath);
                string playerSteamId = player!.SteamID.ToString();

                if (!presets.ContainsKey(playerSteamId))
                {
                    presets[playerSteamId] = new Dictionary<string, SavedBotPositionPreset>();
                }

                presets[playerSteamId][presetName] = new SavedBotPositionPreset
                {
                    Map = Server.MapName,
                    Bots = savedBots
                };

                File.WriteAllText(presetsPath, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true }));
                ReplyToUserCommand(player, $"Saved {savedBots.Count} bot position(s) as '{presetName}'.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log($"[SaveBotPositions] Failed: {ex.Message}");
                ReplyToUserCommand(player, "Unable to save the bot positions.");
            }
        }

        private void HandleLoadBotPositionsCommand(CCSPlayerController? player, string rawPresetName)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            string presetQuery = NormalizeBotPresetName(rawPresetName);
            if (string.IsNullOrWhiteSpace(presetQuery))
            {
                ReplyToUserCommand(player, "Usage: .lbp <name>");
                return;
            }

            try
            {
                string presetsPath = GetSavedBotPositionsPath();
                Dictionary<string, Dictionary<string, SavedBotPositionPreset>> presets = ReadSavedBotPositionPresets(presetsPath);
                string playerSteamId = player!.SteamID.ToString();

                if (!presets.TryGetValue(playerSteamId, out Dictionary<string, SavedBotPositionPreset>? playerPresets))
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{presetQuery}' was not found.");
                    return;
                }

                List<string> presetsOnCurrentMap = playerPresets
                    .Where(preset => preset.Value.Map == Server.MapName)
                    .Select(preset => preset.Key)
                    .ToList();
                string resolvedPresetName = StringSimilarity.FindNearestName(presetQuery, presetsOnCurrentMap);

                if (!presetsOnCurrentMap.Contains(resolvedPresetName) ||
                    !playerPresets.TryGetValue(resolvedPresetName, out SavedBotPositionPreset? preset) ||
                    preset.Bots.Count == 0)
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{presetQuery}' was not found on {Server.MapName}.");
                    return;
                }

                List<SavedBotPosition> botsToRestore = preset.Bots
                    .Where(bot => bot.TeamNum == (byte)CsTeam.Terrorist || bot.TeamNum == (byte)CsTeam.CounterTerrorist)
                    .ToList();
                if (botsToRestore.Count == 0)
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{resolvedPresetName}' contains no valid bots.");
                    return;
                }

                int loadGeneration = BeginBotPresetLoad();
                ReplyToUserCommand(player, $"Loading {botsToRestore.Count} bot position(s) from '{resolvedPresetName}'.");
                AddTimer(
                    0.1f,
                    () => WaitForBotPresetCleanup(player, resolvedPresetName, botsToRestore, loadGeneration, attempt: 0),
                    TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log($"[LoadBotPositions] Failed: {ex.Message}");
                ReplyToUserCommand(player, "Unable to load the bot positions.");
            }
        }

        private void HandleDeleteBotPositionsCommand(CCSPlayerController? player, string rawPresetName)
        {
            if (!IsPlayerValid(player)) return;
            if (!isPractice)
            {
                ReplyToUserCommand(player, ".dbp is available only in practice mode.");
                return;
            }

            string presetQuery = NormalizeBotPresetName(rawPresetName);
            if (string.IsNullOrWhiteSpace(presetQuery))
            {
                ReplyToUserCommand(player, "Usage: .dbp <name>");
                return;
            }

            try
            {
                string presetsPath = GetSavedBotPositionsPath();
                Dictionary<string, Dictionary<string, SavedBotPositionPreset>> presets = ReadSavedBotPositionPresets(presetsPath);
                string playerSteamId = player!.SteamID.ToString();

                if (!presets.TryGetValue(playerSteamId, out Dictionary<string, SavedBotPositionPreset>? playerPresets))
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{presetQuery}' was not found on {Server.MapName}.");
                    return;
                }

                List<string> presetsOnCurrentMap = playerPresets
                    .Where(preset => preset.Value.Map == Server.MapName)
                    .Select(preset => preset.Key)
                    .ToList();
                if (presetsOnCurrentMap.Count == 0)
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{presetQuery}' was not found on {Server.MapName}.");
                    return;
                }

                string resolvedPresetName = StringSimilarity.FindNearestName(presetQuery, presetsOnCurrentMap);

                if (!presetsOnCurrentMap.Contains(resolvedPresetName) || !playerPresets.Remove(resolvedPresetName))
                {
                    ReplyToUserCommand(player, $"Bot-position preset '{presetQuery}' was not found on {Server.MapName}.");
                    return;
                }

                if (playerPresets.Count == 0)
                {
                    presets.Remove(playerSteamId);
                }

                File.WriteAllText(presetsPath, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true }));
                ReplyToUserCommand(player, $"Deleted bot-position preset '{resolvedPresetName}'.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log($"[DeleteBotPositions] Failed: {ex.Message}");
                ReplyToUserCommand(player, "Unable to delete the bot-position preset.");
            }
        }

        private int BeginBotPresetLoad()
        {
            int loadGeneration = ++botPresetLoadGeneration;
            ResetTurretCombatState();
            botHealthCeilings.Clear();
            ApplyBotShootingState();
            Server.ExecuteCommand("bot_kick");
            Server.ExecuteCommand("bot_quota 0");
            Server.ExecuteCommand("bot_dont_shoot 1");
            Server.ExecuteCommand("bot_stop 1");
            Server.ExecuteCommand("bot_freeze 1");
            Server.ExecuteCommand("bot_zombie 1");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            isSpawningBot = true;
            return loadGeneration;
        }

        private void WaitForBotPresetCleanup(
            CCSPlayerController owner,
            string presetName,
            List<SavedBotPosition> savedBots,
            int loadGeneration,
            int attempt)
        {
            if (loadGeneration != botPresetLoadGeneration) return;
            if (!IsPlayerValid(owner))
            {
                isSpawningBot = false;
                ApplyBotShootingState();
                return;
            }

            bool oldBotStillConnected = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
                .Any(bot => bot.IsValid && bot.IsBot && !bot.IsHLTV);
            if (!oldBotStillConnected)
            {
                RestoreNextSavedBot(owner, presetName, savedBots, 0, loadGeneration);
                return;
            }

            if (attempt >= 50)
            {
                isSpawningBot = false;
                ApplyBotShootingState();
                ReplyToUserCommand(owner, $"Unable to clear existing bots before loading '{presetName}'.");
                return;
            }

            if (attempt > 0 && attempt % 10 == 0)
            {
                Server.ExecuteCommand("bot_kick");
            }

            AddTimer(
                0.1f,
                () => WaitForBotPresetCleanup(owner, presetName, savedBots, loadGeneration, attempt + 1),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void RestoreNextSavedBot(CCSPlayerController owner, string presetName, List<SavedBotPosition> savedBots, int index, int loadGeneration)
        {
            if (loadGeneration != botPresetLoadGeneration) return;
            if (!IsPlayerValid(owner))
            {
                isSpawningBot = false;
                ApplyBotShootingState();
                return;
            }

            if (index >= savedBots.Count)
            {
                isSpawningBot = false;
                ApplyBotShootingState();
                ReplyToUserCommand(owner, $"Loaded {savedBots.Count} bot position(s) from '{presetName}'.");
                return;
            }

            SavedBotPosition savedBot = savedBots[index];
            HashSet<int> existingBotUserIds = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
                .Where(bot => bot.IsValid && bot.IsBot && bot.UserId.HasValue)
                .Select(bot => bot.UserId!.Value)
                .ToHashSet();

            string teamName = savedBot.TeamNum == (byte)CsTeam.Terrorist ? "T" : "CT";
            Server.ExecuteCommand($"bot_join_team {teamName}");
            Server.ExecuteCommand(savedBot.TeamNum == (byte)CsTeam.Terrorist ? "bot_add_t" : "bot_add_ct");

            AddTimer(
                0.25f,
                () => TryPlaceRestoredBot(owner, presetName, savedBots, index, loadGeneration, existingBotUserIds, 0),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void TryPlaceRestoredBot(
            CCSPlayerController owner,
            string presetName,
            List<SavedBotPosition> savedBots,
            int index,
            int loadGeneration,
            HashSet<int> existingBotUserIds,
            int attempt)
        {
            if (loadGeneration != botPresetLoadGeneration) return;
            if (!IsPlayerValid(owner))
            {
                isSpawningBot = false;
                ApplyBotShootingState();
                return;
            }

            SavedBotPosition savedBot = savedBots[index];
            List<CCSPlayerController> newBots = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
                .Where(bot => IsPlayerValid(bot) && bot.IsBot && !bot.IsHLTV && bot.UserId.HasValue && !existingBotUserIds.Contains(bot.UserId.Value))
                .ToList();
            CCSPlayerController? restoredBot = newBots.FirstOrDefault(bot => bot.TeamNum == savedBot.TeamNum);

            if (restoredBot == null)
            {
                if (attempt < 40)
                {
                    // bot_add can be ignored while a previous bot client is still finishing
                    // its connection. Reissue it periodically while continuing to watch for
                    // the controller, then clean up any duplicate bot that appears later.
                    if (attempt > 0 && attempt % 10 == 0)
                    {
                        Server.ExecuteCommand(savedBot.TeamNum == (byte)CsTeam.Terrorist ? "bot_add_t" : "bot_add_ct");
                    }

                    AddTimer(
                        0.1f,
                        () => TryPlaceRestoredBot(owner, presetName, savedBots, index, loadGeneration, existingBotUserIds, attempt + 1),
                        TimerFlags.STOP_ON_MAPCHANGE);
                    return;
                }

                isSpawningBot = false;
                ApplyBotShootingState();
                ReplyToUserCommand(owner, $"Unable to create bot {index + 1}/{savedBots.Count} from '{presetName}'.");
                return;
            }

            // Loading a preset is an explicit remove-and-create operation. A newly added bot
            // must be alive for placement even when the session-wide .botrespawn toggle is
            // false; that toggle still controls only subsequent deaths after loading.
            if (!restoredBot.PawnIsAlive)
            {
                if (attempt >= 40)
                {
                    isSpawningBot = false;
                    ApplyBotShootingState();
                    ReplyToUserCommand(owner, $"Unable to spawn bot {index + 1}/{savedBots.Count} from '{presetName}'.");
                    return;
                }

                restoredBot.Respawn();
                AddTimer(
                    0.1f,
                    () => TryPlaceRestoredBot(owner, presetName, savedBots, index, loadGeneration, existingBotUserIds, attempt + 1),
                    TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }

            foreach (CCSPlayerController extraBot in newBots)
            {
                if (extraBot.UserId != restoredBot.UserId)
                {
                    Server.ExecuteCommand($"kickid {extraBot.UserId!.Value}");
                }
            }

            int restoredBotUserId = restoredBot.UserId!.Value;
            Position restoredPosition = savedBot.ToPosition();
            pracUsedBots[restoredBotUserId] = new Dictionary<string, object>
            {
                { "controller", restoredBot },
                { "position", restoredPosition },
                { "owner", owner },
                { "crouchstate", savedBot.Crouched }
            };

            CCSPlayerPawn? pawn = restoredBot.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
            {
                pawn.Teleport(restoredPosition.PlayerPosition, restoredPosition.PlayerAngle, new Vector(0, 0, 0));
                if (savedBot.Crouched)
                {
                    pawn.Flags |= (uint)PlayerFlags.FL_DUCKING;
                    if (pawn.MovementServices != null)
                    {
                        CCSPlayer_MovementServices movementServices = new(pawn.MovementServices.Handle);
                        AddTimer(0.1f, () => movementServices.DuckAmount = 1);
                    }
                    if (pawn.Bot != null)
                    {
                        AddTimer(0.2f, () => pawn.Bot.IsCrouching = true);
                    }
                }
            }

            AddTimer(
                0.2f,
                () => RestoreNextSavedBot(owner, presetName, savedBots, index + 1, loadGeneration),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void RemoveAllPracticeBots()
        {
            botPresetLoadGeneration++;
            isSpawningBot = false;
            ResetTurretCombatState();
            botHealthCeilings.Clear();
            Server.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
        }

        private static string NormalizeBotPresetName(string rawPresetName)
        {
            return string.Join(" ", rawPresetName.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().Trim('"');
        }

        private bool TryGetSavedBotPositionPresetNames(CCSPlayerController player, out List<string> presetNames)
        {
            presetNames = new List<string>();

            try
            {
                Dictionary<string, Dictionary<string, SavedBotPositionPreset>> presets =
                    ReadSavedBotPositionPresets(GetSavedBotPositionsPath());
                if (!presets.TryGetValue(player.SteamID.ToString(), out Dictionary<string, SavedBotPositionPreset>? playerPresets))
                {
                    return true;
                }

                presetNames = playerPresets
                    .Where(preset => preset.Value.Map == Server.MapName)
                    .Select(preset => preset.Key)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log($"[ListBotPositions] Failed: {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, Dictionary<string, SavedBotPositionPreset>> ReadSavedBotPositionPresets(string presetsPath)
        {
            if (!File.Exists(presetsPath)) return new Dictionary<string, Dictionary<string, SavedBotPositionPreset>>();

            string json = File.ReadAllText(presetsPath);
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, SavedBotPositionPreset>>>(json)
                ?? new Dictionary<string, Dictionary<string, SavedBotPositionPreset>>();
        }

        private static string GetSavedBotPositionsPath()
        {
            return Path.Join(Server.GameDirectory, "csgo/cfg/MatchZy/savedbotpositions.json");
        }

        [ConsoleCommand("css_ff", "Fast forwards the timescale to 20 seconds")]
        public void OnFFCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null) return;

            Dictionary<int, MoveType_t> preFastForwardMoveTypes = new();

            foreach (var key in playerData.Keys) {
                if(!IsPlayerValid(playerData[key])) continue;
                preFastForwardMoveTypes[key] = playerData[key].PlayerPawn.Value!.MoveType;
                playerData[key].PlayerPawn.Value!.MoveType = MoveType_t.MOVETYPE_NONE;
            }

            Server.PrintToChatAll($"{chatPrefix} Fastforwarding 20 seconds!");
            Server.ExecuteCommand("host_timescale 10");
            AddTimer(20.0f, () => {
                ResetFastForward(preFastForwardMoveTypes);
            });

        }

        [ConsoleCommand("css_fastforward", "Fast forwards the timescale to 20 seconds")]
        public void OnFastForwardCommand(CCSPlayerController? player, CommandInfo? command)
        {
            OnFFCommand(player, command);
        }

        public void ResetFastForward(Dictionary<int, MoveType_t> preFastForwardMoveTypes) {
            if (!isPractice) return;
            Server.ExecuteCommand("host_timescale 1");
            foreach (var key in playerData.Keys) {
                if(!IsPlayerValid(playerData[key])) continue;
                playerData[key].PlayerPawn.Value!.MoveType = preFastForwardMoveTypes[key];
            }
        }

        [ConsoleCommand("css_clear", "Removes active utility and dropped equipment except the C4")]
        public void OnClearCommand(CCSPlayerController? player, CommandInfo? command)
        {
            ClearPracticeUtilities(player);
        }

        private bool ClearPracticeUtilities(CCSPlayerController? player)
        {
            if (!isPractice || (player != null && !IsPlayerValid(player))) return false;
            RemoveGrenadeEntities();
            RemoveDroppedPracticeEquipment();
            return true;
        }

        [ConsoleCommand("css_spec", "Switches team to Spectator")]
        public void OnSpecCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player == null) return;

            SideSwitchCommand(player, CsTeam.Spectator);
        }

        [ConsoleCommand("css_fas", "Switches all other players to spectator")]
        [ConsoleCommand("css_watchme", "Switches all other players to spectator")]
        public void OnFASCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player == null) return;

            SideSwitchCommand(player, CsTeam.None);
        }

        [ConsoleCommand("css_noblind", "Disables flash effect for the player")]
        [ConsoleCommand("css_noflash", "Disables flash effect for the player")]
        public void OnNoFlashCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player == null || player.UserId == null) return;

            int userId = player.UserId.Value;
            SetPracticeFlashProtection(player, !noFlashList.Contains(userId));
        }

        private bool SetPracticeFlashProtection(CCSPlayerController? player, bool enabled)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV || !player.UserId.HasValue) return false;

            int userId = player.UserId.Value;
            if (enabled)
            {
                if (!noFlashList.Contains(userId)) noFlashList.Add(userId);
                ReplyToUserCommand(player, "Enabled noflash. Use .noflash again to disable.");
                Server.NextFrame(() => KillFlashEffect(player));
            }
            else
            {
                noFlashList.Remove(userId);
                ReplyToUserCommand(player, "Disabled noflash.");
            }

            return true;
        }

        [ConsoleCommand("css_break", "Breaks the breakable entities")]
        public void OnBreakCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            var entities = Utilities.FindAllEntitiesByDesignerName<CBreakable>("prop_dynamic")
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_breakable"));
            foreach (var entity in entities)
            {
                entity.AcceptInput("Break");
            }
        }

        public void KillFlashEffect(CCSPlayerController player) {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) return;
            Log($"[KillFlashEffect] Killing flash effect for player: {player.PlayerName}");
            playerPawn.FlashMaxAlpha = 0.5f;
        }

        // CsTeam.None is a special value to mean force all other players to spectator
        private bool SwitchPracticePlayerSide(CCSPlayerController? player, CsTeam team)
        {
            if (!isPractice || !IsPlayerValid(player) || player!.IsBot || player.IsHLTV) return false;
            return SideSwitchCommand(player, team);
        }

        private bool SideSwitchCommand(CCSPlayerController player, CsTeam team) {
          if (team > CsTeam.None) {
            if(player.TeamNum == (byte)CsTeam.Spectator) {
              // ReplyToUserCommand(player, "Switching to a team from spectator is currently broken, use the team menu.");
              ReplyToUserCommand(player, Localizer["matchzy.pm.spectatorbroken"]);
              return false;
            }
            player.ChangeTeam(team);
            return true;
          }
          Utilities.GetPlayers().ForEach((x) => { 
              if(x.IsValid && !x.IsBot && x.UserId != player.UserId) {
                x.ChangeTeam(CsTeam.Spectator);
              }
            });
            return true;
        }

        public void RemoveGrenadeEntities()
        {
            if (!isPractice) return;
            var smokes = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
            foreach (var entity in smokes)
            {
                entity?.Remove();
            }
            var mollys = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("molotov_projectile");
            foreach (var entity in mollys)
            {
                entity?.Remove();
            }
            var inferno = Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("inferno");
            foreach (var entity in inferno)
            {
                entity?.Remove();
            }
        }

        private void RemoveDroppedPracticeEquipment()
        {
            if (!isPractice) return;

            foreach (CEntityInstance entity in Utilities.GetAllEntities().ToList())
            {
                if (!entity.IsValid) continue;

                string designerName = entity.DesignerName;
                bool isDefuseKit = designerName.Equals("item_defuser", StringComparison.OrdinalIgnoreCase);
                bool isWeapon = designerName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase);

                // Preserve both carried and dropped C4. Planted bombs use a different
                // designer name and therefore never enter this weapon branch either.
                if ((!isDefuseKit && !isWeapon) ||
                    designerName.Equals("weapon_c4", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CBaseEntity groundItem = entity.As<CBaseEntity>();
                if (groundItem.OwnerEntity.IsValid) continue;

                entity.Remove();
            }
        }

        public void ExecDryRunCFG()
        {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", dryrunCfgPath);
    
            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath)) {
                Log($"[ExecDryRunCFG] Starting Dryrun! Executing Dryrun CFG from {dryrunCfgPath}");
                Server.ExecuteCommand($"exec {dryrunCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[ExecDryRunCFG] Starting Dryrun! Dryrun CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 6;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 3;mp_team_timeout_ot_max 1;mp_team_timeout_ot_add_each 1;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
        }

        public void ExecUnpracCommands() {
            CloseAllConfigurationMenus();
            ResetTurretCombatState();
            ResetPracticeHumanGodModes();
            botShootingEnabled = false;
            botRespawnEnabled = true;
            botLifeRegenerationEnabled = false;
            humanLifeRegenerationEnabled.Clear();
            botReactionTimeMs = DefaultBotReactionTimeMs;
            botHealthCeilings.Clear();
            botNextRegenerationTime = 0.0f;
            humanNextRegenerationTimes.Clear();
            practiceRoundTimeoutEnding = false;
            Server.ExecuteCommand("bot_stop 0; bot_freeze 0; bot_zombie 0; bot_dont_shoot 0");
            Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
            Server.ExecuteCommand("sv_cheats false;sv_grenade_trajectory_prac_pipreview false;sv_grenade_trajectory_prac_trailtime 0; mp_ct_default_grenades \"\"; mp_ct_default_primary \"\"; mp_t_default_grenades\"\"; mp_t_default_primary\"\"; mp_teammates_are_enemies false;");
            Server.ExecuteCommand("mp_death_drop_breachcharge true; mp_death_drop_defuser true; mp_death_drop_taser true; mp_drop_knife_enable false; mp_death_drop_grenade 2; ammo_grenade_limit_total 4; mp_defuser_allocation 0; sv_infinite_ammo 0; mp_force_pick_time 15");
        }

        public bool IsValidPositionForLastGrenade(CCSPlayerController player, int position)
        {
            int userId = player.UserId!.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                // PrintToPlayerChat(player, $"You have not thrown any nade yet!");
                PrintToPlayerChat(player, Localizer["matchzy.pm.nothrownnades"]);
                return false;
            }

            if (lastGrenadesData[userId].Count < position)
            {
                // PrintToPlayerChat(player, $"Your grenade history only goes from 1 to {lastGrenadesData[userId].Count}!");
                PrintToPlayerChat(player, Localizer["matchzy.pm.grenadehistory", $"{lastGrenadesData[userId].Count}"]);
                return false;
            }

            return true;
        }

        private bool RethrowSpecificPracticeUtility(CCSPlayerController? player, PracticeGrenadeType grenadeType)
        {
            if (player == null) return false;
            string nadeType = grenadeType switch
            {
                PracticeGrenadeType.Smoke => "smoke",
                PracticeGrenadeType.Flash => "flash",
                PracticeGrenadeType.Molotov => "molotov",
                PracticeGrenadeType.Decoy => "decoy",
                _ => throw new ArgumentOutOfRangeException(nameof(grenadeType))
            };
            return RethrowSpecificNade(player, nadeType);
        }

        public bool RethrowSpecificNade(CCSPlayerController player, string nadeType)
        {
            if (!isPractice || !IsPlayerValid(player) || !player.UserId.HasValue) return false;
            int userId = player.UserId.Value;
            if (!nadeSpecificLastGrenadeData.ContainsKey(userId) || !nadeSpecificLastGrenadeData[userId].ContainsKey(nadeType))
            {
                // PrintToPlayerChat(player, $"You have not thrown any {nadeType} yet!");
                PrintToPlayerChat(player, Localizer["matchzy.pm.nothrownnadestype", nadeType]);
                return false;
            }
            GrenadeThrownData grenadeThrown = nadeSpecificLastGrenadeData[userId][nadeType];
            AddTimer(grenadeThrown.Delay, () =>
            {
                if (IsPlayerValid(player) && player.UserId == userId)
                {
                    grenadeThrown.Throw(player);
                }
            });
            return true;
        }

        public void HandleBackCommand(CCSPlayerController player, string number)
        {
            if (!isPractice || player == null || !player.UserId.HasValue) return;
            int userId = player.UserId.Value;
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (int.TryParse(number, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        lastGrenadesData[userId][positionNumber].LoadPosition(player);
                        // PrintToPlayerChat(player, $"Teleported to grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(player, Localizer["matchzy.pm.tptogrenade", $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"]);
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"Invalid value for !back command. Please specify a valid non-negative number. Usage: !back <number>");
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backinvalidvalue"]);
                    return;
                }
            }
            else
            {
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                // ReplyToUserCommand(player, $"Usage: !back <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.backtonumber", thrownCount]);
            }
        }

        public void HandleThrowIndexCommand(CCSPlayerController player, string argString)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            int userId = player!.UserId!.Value;

            if (string.IsNullOrEmpty(argString))
            {
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                // ReplyToUserCommand(player, $"Usage: !throwindex <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.throwindextonumber", thrownCount]);
                return;
            }

            string[] argsList = argString.Split();

            foreach (string arg in argsList)
            {
                if (int.TryParse(arg, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        GrenadeThrownData grenadeThrown = lastGrenadesData[userId][positionNumber];
                        AddTimer(grenadeThrown.Delay, () =>
                        {
                            if (IsPlayerValid(player) && player.UserId == userId)
                            {
                                grenadeThrown.Throw(player);
                            }
                        });
                        // PrintToPlayerChat(player, $"Throwing grenade of history position: {positionNumber+1}/{lastGrenadesData[userId].Count}");
                        PrintToPlayerChat(player, Localizer["matchzy.pm.throwgrenadehistory", $"{positionNumber + 1}/{lastGrenadesData[userId].Count}"]);
                    }
                }
                else
                {
                    // PrintToPlayerChat(player, $"'{arg}' is not a valid non-negative number for !throwindex command.");
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backnegativenumber", arg]);
                }
            }
        }

        public void HandleDelayCommand(CCSPlayerController player, string delay)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            if (!isPractice || player == null || !player.UserId.HasValue) return;
            int userId = player.UserId.Value;
            if (string.IsNullOrWhiteSpace(delay))
            {
                // ReplyToUserCommand(player, $"Usage: !delay <delay_in_seconds>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
                return;
            }
            
            if (float.TryParse(delay, out float delayInSeconds) && delayInSeconds > 0)
            {
                if (IsValidPositionForLastGrenade(player, 0))
                {
                    lastGrenadesData[userId].Last().Delay = delayInSeconds;
                    // PrintToPlayerChat(player, $"Delay of {delayInSeconds:0.00}s set for grenade of index: {lastGrenadesData[userId].Count}.");
                    PrintToPlayerChat(player, Localizer["matchzy.pm.delaygrenade", $"{delayInSeconds:0.00}", $"{lastGrenadesData[userId].Count}"]);
                }
            }
            else
            {
                // PrintToPlayerChat(player, $"Delay of {delayInSeconds:0.00}s set for grenade of index: {lastGrenadesData[userId].Count}.);
                PrintToPlayerChat(player, Localizer["matchzy.pm.delayvalidnumber", $"{delayInSeconds:0.00}", $"{lastGrenadesData[userId].Count}"]);
                return;
            }
        }

        public void DisplayPracticeTimerCenter(int userId)
        {
            if (!playerData.ContainsKey(userId) || !playerTimers.ContainsKey(userId)) return;
            if (!IsPlayerValid(playerData[userId])) return;
            playerTimers[userId].DisplayTimerCenter(playerData[userId]);
        }

        [ConsoleCommand("css_throw", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rethrow", "Throws the last thrown grenade")]
        public void OnRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowLastPracticeUtility(player);
        }

        private bool RethrowLastPracticeUtility(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue) return false;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                // PrintToPlayerChat(player, $"You have not thrown any nade yet!");
                PrintToPlayerChat(player, Localizer["matchzy.pm.notthrownnade"]);
                return false;
            }

            DateTime now = DateTime.UtcNow;
            if (lastRethrowCommandTime.TryGetValue(userId, out DateTime lastRethrow) &&
                now - lastRethrow < TimeSpan.FromMilliseconds(500))
            {
                return false;
            }

            GrenadeThrownData lastGrenade = lastGrenadesData[userId].Last();
            CCSPlayerController validPlayer = player;

            lastRethrowCommandTime[userId] = now;
            AddTimer(lastGrenade.Delay, () =>
            {
                if (IsPlayerValid(validPlayer) && validPlayer.UserId == userId)
                {
                    lastGrenade.Throw(validPlayer);
                }
            });
            return true;
        }

        [ConsoleCommand("css_grt", "Globally rethrows the last grenade thrown on the server")]
        public void OnGlobalRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowGlobalLastUtility(player);
        }

        private bool RethrowGlobalLastUtility(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue) return false;

            int userId = player.UserId.Value;
            DateTime now = DateTime.UtcNow;
            if (lastGlobalRethrowCommandTime.TryGetValue(userId, out DateTime lastRethrow) &&
                now - lastRethrow < TimeSpan.FromMilliseconds(500))
            {
                return false;
            }

            lastGlobalRethrowCommandTime[userId] = now;

            // This is intentionally the fixed, server-global Valve command. Keep it
            // separate from .rt/.rethrow, which use the requesting player's history.
            Server.ExecuteCommand("sv_rethrow_last_grenade");
            return true;
        }

        [ConsoleCommand("css_slp", "Saves the player's current location and view direction")]
        [ConsoleCommand("css_savepos", "Saves the player location")]
        public void OnSavePosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            SavePracticePlayerPosition(player);
        }

        private bool SavePracticePlayerPosition(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue || player.PlayerPawn.Value == null) return false;

            int userId = player.UserId.Value;
            var pawn = player.PlayerPawn.Value;
            Vector position = new(pawn.AbsOrigin?.X, pawn.AbsOrigin?.Y, pawn.AbsOrigin?.Z);
            QAngle angle = new(pawn.EyeAngles?.X, pawn.EyeAngles?.Y, pawn.EyeAngles?.Z);
            
            savedPlayerLocationData[userId] = new PlayerLocationData(position, angle);
            Log($"[SavePos] Saved position for UserID {userId}, Position: {position}, Angle: {angle}!");
            PrintToPlayerChat(player, Localizer["matchzy.pm.savepos"]);
            return true;
        }

        [ConsoleCommand("css_tlp", "Teleports the player to their last saved location")]
        [ConsoleCommand("css_loadpos", "Loads the last saved player location")]
        public void OnLoadPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            LoadPracticePlayerPosition(player);
        }

        private bool LoadPracticePlayerPosition(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue) return false;

            int userId = player.UserId.Value;
            if (!savedPlayerLocationData.TryGetValue(userId, out var playerLocationData))
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.notsavedpos"]);
                return false;
            }
            
            Log($"[LoadPos] LoadPos position for UserID {userId}, Position: {playerLocationData.Position}, Angles: {playerLocationData.Angle}!");
            playerLocationData.LoadPosition(player);
            PrintToPlayerChat(player, Localizer["matchzy.pm.loadpos"]);
            return true;
        }

        [ConsoleCommand("css_dlp", "Deletes the player's saved location")]
        public void OnDeletePosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            DeletePracticePlayerPosition(player);
        }

        private bool DeletePracticePlayerPosition(CCSPlayerController? player)
        {
            if (!isPractice || !IsPlayerValid(player) || !player!.UserId.HasValue) return false;

            if (savedPlayerLocationData.Remove(player.UserId.Value))
            {
                PrintToPlayerChat(player, "Saved position deleted.");
                return true;
            }

            PrintToPlayerChat(player, Localizer["matchzy.pm.notsavedpos"]);
            return false;
        }

        [ConsoleCommand("css_throwsmoke", "Throws the last thrown smoke")]
        [ConsoleCommand("css_rethrowsmoke", "Throws the last thrown smoke")]
        public void OnRethrowSmokeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Smoke);
        }

        [ConsoleCommand("css_throwflash", "Throws the last thrown flash")]
        [ConsoleCommand("css_rethrowflash", "Throws the last thrown flash")]
        public void OnRethrowFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Flash);
        }

        [ConsoleCommand("css_throwgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrowgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_thrownade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrownade", "Throws the last thrown he grenade")]
        public void OnRethrowGrenadeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null) return;
            RethrowSpecificNade(player, "hegrenade");
        }

        [ConsoleCommand("css_throwmolotov", "Throws the last thrown molotov")]
        [ConsoleCommand("css_rethrowmolotov", "Throws the last thrown molotov")]
        public void OnRethrowMolotovCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Molotov);
        }

        [ConsoleCommand("css_throwdecoy", "Throws the last thrown decoy")]
        [ConsoleCommand("css_rethrowdecoy", "Throws the last thrown decoy")]
        public void OnRethrowDecoyCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Decoy);
        }

        [ConsoleCommand("css_last", "Teleports to the last thrown grenade position")]
        public void OnLastCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue) return;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.ContainsKey(userId) || lastGrenadesData[userId].Count <= 0)
            {
                // PrintToPlayerChat(player, $"You have not thrown any nade yet!");
                PrintToPlayerChat(player, Localizer["matchzy.pm.notthrownnade"]);
                return;
            }
            lastGrenadesData[userId].Last().LoadPosition(player);
        }

        [ConsoleCommand("css_back", "Teleports to the provided position in grenade thrown history")]
        public void OnBackCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || player == null || !player.UserId.HasValue) return;
            if (command.ArgCount >= 2) 
            {
                string commandArg = command.ArgByIndex(1);
                HandleBackCommand(player, commandArg);
            }
            else 
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                // ReplyToUserCommand(player, $"Usage: !back <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.backtonumber", thrownCount]);
            }      
        }

        [ConsoleCommand("css_throwidx", "Throws grenade of provided position in grenade thrown history")]
        [ConsoleCommand("css_throwindex", "Throws grenade of provided position in grenade thrown history")]
        public void OnThrowIndexCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (command.ArgCount >= 2) 
            {
                HandleThrowIndexCommand(player!, command.ArgString);
            }
            else 
            {
                int userId = player!.UserId!.Value;
                int thrownCount = lastGrenadesData.ContainsKey(userId) ? lastGrenadesData[userId].Count : 0;
                // ReplyToUserCommand(player, $"Usage: !throwindex <number> (You've thrown {thrownCount} grenades till now)");
                ReplyToUserCommand(player, Localizer["matchzy.pm.throwindextonumber", thrownCount]);
            }      
        }

        [ConsoleCommand("css_lastindex", "Returns index of the last thrown grenade")]
        public void OnLastIndexCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (IsValidPositionForLastGrenade(player!, 1))
            {
                // PrintToPlayerChat(player!, $"Index of last thrown grenade: {lastGrenadesData[player!.UserId!.Value].Count}");
                PrintToPlayerChat(player!, Localizer["matchzy.pm.indexlastgrenade", $"{lastGrenadesData[player!.UserId!.Value].Count}"]);
            } 
        }

        [ConsoleCommand("css_delay", "Adds a delay to the last thrown grenade. Usage: !delay <delay_in_seconds>")]
        public void OnDelayCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            if (command.ArgCount >= 2) 
            {
                HandleDelayCommand(player!, command.ArgByIndex(1));
            }
            else 
            {
                // ReplyToUserCommand(player, $"Usage: !delay <delay_in_seconds>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
            }      
        }

        [ConsoleCommand("css_timer", "Starts a timer, use .timer again to stop it.")]
        public void OnTimerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            int userId = player!.UserId!.Value;
            if (playerTimers.ContainsKey(userId))
            {
                playerTimers[userId].KillTimer();
                double timerResult = playerTimers[userId].GetTimerResult();
                player.PrintToCenter($"Timer: {timerResult}s");
                PrintToPlayerChat(player, $"Timer stopped! Result: {timerResult}s");
                playerTimers.Remove(userId);
            }
            else
            {
                playerTimers[userId] = new PlayerPracticeTimer(PracticeTimerType.Immediate)
                {
                    StartTime = DateTime.Now,
                    Timer = AddTimer(0.1f, () => DisplayPracticeTimerCenter(userId), TimerFlags.REPEAT)
                };
                PrintToPlayerChat(player, $"Timer started! User !timer to stop it.");
            }
        }

        [ConsoleCommand("css_sn", "Saves current nade position")]
        [ConsoleCommand("css_savenade", "Saves current nade position")]
        public void OnSaveNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleSaveNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_ln", "Loades the nade with provided filter")]
        [ConsoleCommand("css_loadnade", "Loades the nade with provided filter")]
        public void OnLoadNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleLoadNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_lin", "Lists the nade with provided filter")]
        [ConsoleCommand("css_listnades", "Lists the nade with provided filter")]
        public void OnListNadesCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleListNadesCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_importnade", "Imports the nade with the given code")]
        [ConsoleCommand("css_in", "Imports the nade with the given code")]
        public void OnImportNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleImportNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_deletenade", "Deletes the nade by name")]
        [ConsoleCommand("css_delnade", "Deletes the nade by name")]
        [ConsoleCommand("css_dn", "Deletes the nade by name")]
        public void OnDeleteNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleDeleteNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_solid", "Toggles mp_solid_teammates in practice mode")]
        public void OnSolidCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            int solidValue = ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>();

            int newSolidValue = (solidValue == 0 || solidValue == 1) ? 2 : 1;

            ConVar.Find("mp_solid_teammates")!.SetValue(newSolidValue);

            PrintToAllChat($"mp_solid_teammates is now set to {newSolidValue}");
        }

        [ConsoleCommand("css_impacts", "Toggles sv_showimpacts in practice mode")]
        public void OnImpactsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            int impactValue = ConVar.Find("sv_showimpacts")!.GetPrimitiveValue<int>();

            int newImpactValue = 1 - impactValue;

            Server.ExecuteCommand($"sv_showimpacts {newImpactValue}");

            PrintToAllChat($"sv_showimpacts is now set to {newImpactValue}");
        }

        [ConsoleCommand("css_traj", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        [ConsoleCommand("css_pip", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        public void OnTrajCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            bool trajValue = ConVar.Find("sv_grenade_trajectory_prac_pipreview")!.GetPrimitiveValue<bool>();

            Server.ExecuteCommand($"sv_grenade_trajectory_prac_pipreview {!trajValue}");

            PrintToAllChat($"sv_grenade_trajectory_prac_pipreview is now set to {!trajValue}");
        }

        [ConsoleCommand("css_bestspawn", "Teleports you to your team's closest spawn from your current position")]
        public void OnBestSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToBestSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand("css_worstspawn", "Teleports you to your team's furthest spawn from your current position")]
        public void OnWorstSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToWorstSpawn(player!, player!.TeamNum);
        }

        [ConsoleCommand("css_bestctspawn", "Teleports you to CT team's closest spawn from your current position")]
        public void OnBestCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_worstctspawn", "Teleports you to CT team's furthest spawn from your current position")]
        public void OnWorstCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_besttspawn", "Teleports you to T team's closest spawn from your current position")]
        public void OnBestTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToBestSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_worsttspawn", "Teleports you to T team's furthest spawn from your current position")]
        public void OnWorstTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_spawnmarkers", "Toggles the competitive spawn markers")]
        public void OnSpawnMarkersCommand(CCSPlayerController? player, CommandInfo? command)
        {
            TogglePracticeSpawnMarkers(player);
        }

        private bool TogglePracticeSpawnMarkers(CCSPlayerController? player)
        {
            return SetPracticeSpawnMarkersVisible(player, !spawnMarkersEnabled);
        }

        private bool SetPracticeSpawnMarkersVisible(CCSPlayerController? player, bool visible)
        {
            if (!isPractice || !IsPlayerValid(player)) return false;

            if (visible)
            {
                spawnMarkersEnabled = true;
                ShowSpawnMarkers();
                ReplyToUserCommand(player, $"Spawn outlines shown: {spawnsData[(byte)CsTeam.CounterTerrorist].Count} CT and {spawnsData[(byte)CsTeam.Terrorist].Count} T. Stand inside an outline and press E to teleport.");
            }
            else
            {
                RemoveSpawnMarkers();
            }

            return true;
        }

        public void TeleportPlayerToBestSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns)) return;
            Vector playerPosition = player!.PlayerPawn!.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
            int closestIndex = -1;
            double minDistance = double.MaxValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }
            PlayerTeleport.TeleportSafely(player, teamSpawns[closestIndex].PlayerPosition, teamSpawns[closestIndex].PlayerAngle);
        }

        public void TeleportPlayerToWorstSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns)) return;
            Vector playerPosition = player!.PlayerPawn!.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
            int farthestIndex = -1;
            double maxDistance = double.MinValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestIndex = index;
                }
            }
            PlayerTeleport.TeleportSafely(player, teamSpawns[farthestIndex].PlayerPosition, teamSpawns[farthestIndex].PlayerAngle);
        }

        // Todo: Implement timer2 when we have OnPlayerRunCmd in CS#. Using OnTick would be its alternative, but it would be very expensive and not worth it.
        // [ConsoleCommand("css_timer2", "Starts a timer, use .timer2 again to stop it.")]
        // public void OnTimer2Command(CCSPlayerController? player, CommandInfo command)
        // {
        //     if (!isPractice || !IsPlayerValid(player)) return;
        //     int userId = player!.UserId!.Value;
        //     if (playerTimers.ContainsKey(userId))
        //     {
        //         PrintToPlayerChat(player, $"Timer stopped! Result: {playerTimers[userId].GetTimerResult()}s");
        //         playerTimers[userId].KillTimer();
        //         playerTimers.Remove(userId);
        //     }
        //     else
        //     {
        //         playerTimers[userId] = new PlayerPracticeTimer(PracticeTimerType.OnMovement);
        //         PrintToPlayerChat(player, $"When you start moving a timer will run until you stop moving.");
        //     }
        // }
    }
}
