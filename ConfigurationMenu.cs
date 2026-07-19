using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Net;
using System.Text;

namespace MatchZy;

internal enum PracticeGrenadeType
{
    Smoke,
    Flash,
    Molotov,
    Decoy
}

public partial class MatchZy
{
    private enum ConfigurationMenuScreen
    {
        Root,
        BotPlacement,
        BotPositions,
        DeleteBotPositions,
        BotConfiguration,
        PlayerConfiguration,
        Spawns,
        Utility
    }

    private enum ConfigurationMenuRowId
    {
        BotPlacement,
        BotConfiguration,
        PlayerConfiguration,
        Spawns,
        Utility,
        StartPractice,
        StartRound,
        Close,
        Back,
        PlaceBot,
        PlaceCrouchBot,
        BoostBot,
        CrouchBoostBot,
        ClearAllBots,
        ListBotPositions,
        LoadBotPosition,
        DeleteBotPositions,
        DeleteBotPosition,
        BotShooting,
        BotReactionTime,
        BotRespawn,
        BotLifeRegeneration,
        HumanLifeRegeneration,
        GodMode,
        FlashProtection,
        StorePosition,
        TeleportPosition,
        DeletePosition,
        ToggleSpawnMarkers,
        ChangeToT,
        ChangeToCT,
        RethrowUtility,
        ClearUtilities,
        RethrowSmoke,
        RethrowFlash,
        RethrowMolotov,
        RethrowDecoy,
        GlobalRethrow
    }

    private sealed class ConfigurationMenuSession
    {
        public required CCSPlayerController Player { get; init; }
        public required int UserId { get; init; }
        public ConfigurationMenuScreen Screen { get; set; } = ConfigurationMenuScreen.Root;
        public Dictionary<ConfigurationMenuScreen, int> SelectedRows { get; } = new();
        public Dictionary<ConfigurationMenuScreen, int> ScrollStarts { get; } = new();
        public List<string> BotPositionPresetNames { get; set; } = new();
        public DateTime LastAcceptedInputUtc { get; set; } = DateTime.MinValue;
        public string? Feedback { get; set; }
        public DateTime FeedbackExpiresUtc { get; set; }
        public string? LastRenderedHtml { get; set; }
    }

    private sealed record ConfigurationMenuRow(
        ConfigurationMenuRowId Id,
        string Label,
        string? Value = null,
        bool Enabled = true,
        string? Help = null,
        string? BotPositionPresetName = null);

    private const int ConfigurationMenuVisibleRows = 5;
    private static readonly TimeSpan ConfigurationMenuInputDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ConfigurationMenuFeedbackDuration = TimeSpan.FromSeconds(2.5);
    private readonly Dictionary<int, ConfigurationMenuSession> configurationMenuSessions = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? configurationMenuRefreshTimer;
    private CCSGameRules? configurationMenuGameRules;

    [ConsoleCommand("css_menu", "Toggles the MatchZy-Refined practice configuration menu")]
    public void OnConfigurationMenuCommand(CCSPlayerController? player, CommandInfo? command)
    {
        ToggleConfigurationMenu(player);
    }

    private void ToggleConfigurationMenu(CCSPlayerController? player)
    {
        if (!IsConfigurationMenuPlayerValid(player)) return;

        int userId = player!.UserId!.Value;
        if (configurationMenuSessions.ContainsKey(userId))
        {
            CloseConfigurationMenu(userId, clearDisplay: true);
            return;
        }

        configurationMenuSessions[userId] = new ConfigurationMenuSession
        {
            Player = player,
            UserId = userId
        };

        EnsureConfigurationMenuRefreshTimer();
        UpdateConfigurationMenuHtml(configurationMenuSessions[userId]);
    }

    private static bool IsConfigurationMenuPlayerValid(CCSPlayerController? player)
    {
        return player != null &&
            player.IsValid &&
            !player.IsBot &&
            !player.IsHLTV &&
            player.UserId.HasValue;
    }

    private void EnsureConfigurationMenuRefreshTimer()
    {
        configurationMenuRefreshTimer ??= AddTimer(
            0.25f,
            RefreshConfigurationMenus,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void RefreshConfigurationMenus()
    {
        foreach (ConfigurationMenuSession session in configurationMenuSessions.Values.ToList())
        {
            if (!configurationMenuSessions.TryGetValue(session.UserId, out ConfigurationMenuSession? activeSession) ||
                !ReferenceEquals(activeSession, session))
            {
                continue;
            }

            if (!IsConfigurationMenuPlayerValid(session.Player) ||
                session.Player.UserId != session.UserId)
            {
                CloseConfigurationMenu(session.UserId, clearDisplay: true);
                continue;
            }

            try
            {
                if (!isPractice && session.Screen != ConfigurationMenuScreen.Root)
                {
                    session.Screen = ConfigurationMenuScreen.Root;
                }

                UpdateConfigurationMenuHtml(session);
            }
            catch (Exception exception)
            {
                Log($"[ConfigurationMenu] Failed to render for UserID {session.UserId}: {exception}");
                CloseConfigurationMenu(session.UserId, clearDisplay: true);
            }
        }

        StopConfigurationMenuRefreshIfIdle();
    }

    private void StopConfigurationMenuRefreshIfIdle()
    {
        if (configurationMenuSessions.Count != 0 || configurationMenuRefreshTimer == null) return;
        configurationMenuRefreshTimer.Kill();
        configurationMenuRefreshTimer = null;
    }

    private void CloseConfigurationMenu(int userId, bool clearDisplay)
    {
        if (!configurationMenuSessions.Remove(userId, out ConfigurationMenuSession? session)) return;

        if (clearDisplay && session.Player.IsValid)
        {
            try
            {
                // CounterStrikeSharp's own CenterHtmlMenu uses a single space to
                // clear the complete survival-status HUD panel. An empty string can
                // leave the panel bar visible after the text has disappeared.
                session.Player.PrintToCenterHtml(" ");
            }
            catch (Exception exception)
            {
                Log($"[ConfigurationMenu] Failed to clear display for UserID {userId}: {exception.Message}");
            }
        }

        StopConfigurationMenuRefreshIfIdle();
    }

    private void CloseConfigurationMenu(CCSPlayerController? player, bool clearDisplay = true)
    {
        if (player?.UserId is int userId)
        {
            CloseConfigurationMenu(userId, clearDisplay);
        }
    }

    private void CloseAllConfigurationMenus(bool clearDisplays = true)
    {
        foreach (int userId in configurationMenuSessions.Keys.ToList())
        {
            CloseConfigurationMenu(userId, clearDisplays);
        }

        configurationMenuRefreshTimer?.Kill();
        configurationMenuRefreshTimer = null;
        configurationMenuGameRules = null;
    }

    private bool HandleConfigurationMenuInput(CCSPlayerController player, PlayerButtons pressed)
    {
        if (!player.UserId.HasValue ||
            !configurationMenuSessions.TryGetValue(player.UserId.Value, out ConfigurationMenuSession? session))
        {
            return false;
        }

        if (!IsConfigurationMenuPlayerValid(player) || session.Player.Handle != player.Handle)
        {
            CloseConfigurationMenu(session.UserId, clearDisplay: true);
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (now - session.LastAcceptedInputUtc < ConfigurationMenuInputDebounce) return true;

        int direction = 0;
        if ((pressed & PlayerButtons.Forward) != 0) direction = -1;
        else if ((pressed & PlayerButtons.Back) != 0) direction = 1;
        else if ((pressed & PlayerButtons.Moveleft) != 0) direction = -2;
        else if ((pressed & PlayerButtons.Moveright) != 0) direction = 2;
        else return true;

        session.LastAcceptedInputUtc = now;

        try
        {
            List<ConfigurationMenuRow> rows = BuildConfigurationMenuRows(session);
            int selected = GetSelectedRow(session, rows.Count);

            if (direction == -1 || direction == 1)
            {
                selected = (selected + direction + rows.Count) % rows.Count;
                session.SelectedRows[session.Screen] = selected;
            }
            else
            {
                ApplyConfigurationMenuRow(session, rows[selected], direction > 0);
            }

            if (configurationMenuSessions.ContainsKey(session.UserId))
            {
                UpdateConfigurationMenuHtml(session);
            }
        }
        catch (Exception exception)
        {
            Log($"[ConfigurationMenu] Input failed for UserID {session.UserId}: {exception}");
            CloseConfigurationMenu(session.UserId, clearDisplay: true);
        }

        return true;
    }

    private List<ConfigurationMenuRow> BuildConfigurationMenuRows(ConfigurationMenuSession session)
    {
        CCSPlayerController player = session.Player;
        int userId = session.UserId;

        return session.Screen switch
        {
            ConfigurationMenuScreen.Root => BuildRootConfigurationRows(),
            ConfigurationMenuScreen.BotPlacement =>
            [
                new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back")),
                new(ConfigurationMenuRowId.PlaceBot, MenuText("matchzy.menu.place_bot")),
                new(ConfigurationMenuRowId.PlaceCrouchBot, MenuText("matchzy.menu.place_crouch_bot")),
                new(ConfigurationMenuRowId.BoostBot, MenuText("matchzy.menu.boost_bot")),
                new(ConfigurationMenuRowId.CrouchBoostBot, MenuText("matchzy.menu.crouch_boost_bot")),
                new(ConfigurationMenuRowId.ClearAllBots, MenuText("matchzy.menu.clear_all_bots")),
                new(ConfigurationMenuRowId.ListBotPositions, MenuText("matchzy.menu.list_bot_positions")),
                new(ConfigurationMenuRowId.DeleteBotPositions, MenuText("matchzy.menu.delete_bot_positions"))
            ],
            ConfigurationMenuScreen.BotPositions => BuildBotPositionRows(session),
            ConfigurationMenuScreen.DeleteBotPositions => BuildDeleteBotPositionRows(session),
            ConfigurationMenuScreen.BotConfiguration =>
            [
                new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back")),
                BooleanRow(ConfigurationMenuRowId.BotShooting, "matchzy.menu.bot_shooting", botShootingEnabled),
                new(ConfigurationMenuRowId.BotReactionTime, MenuText("matchzy.menu.bot_reaction_time"), $"{botReactionTimeMs} {MenuText("matchzy.menu.ms")}"),
                BooleanRow(ConfigurationMenuRowId.BotRespawn, "matchzy.menu.bot_respawn", botRespawnEnabled),
                BooleanRow(ConfigurationMenuRowId.BotLifeRegeneration, "matchzy.menu.bot_hp_regeneration", botLifeRegenerationEnabled)
            ],
            ConfigurationMenuScreen.PlayerConfiguration => BuildPlayerConfigurationRows(userId),
            ConfigurationMenuScreen.Spawns => BuildSpawnsRows(player),
            ConfigurationMenuScreen.Utility => BuildUtilityRows(userId),
            _ => []
        };
    }

    private List<ConfigurationMenuRow> BuildRootConfigurationRows()
    {
        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.StartPractice, MenuText("matchzy.menu.start_practice"))
        ];

        if (isPractice)
        {
            rows.Add(new(ConfigurationMenuRowId.StartRound, MenuText("matchzy.menu.start_round")));
            rows.Add(new(ConfigurationMenuRowId.BotPlacement, MenuText("matchzy.menu.bot_placement")));
            rows.Add(new(ConfigurationMenuRowId.BotConfiguration, MenuText("matchzy.menu.bot_configuration")));
            rows.Add(new(ConfigurationMenuRowId.PlayerConfiguration, MenuText("matchzy.menu.player_configuration")));
            rows.Add(new(ConfigurationMenuRowId.Spawns, MenuText("matchzy.menu.spawns")));
            rows.Add(new(ConfigurationMenuRowId.Utility, MenuText("matchzy.menu.utility")));
        }

        rows.Add(new(ConfigurationMenuRowId.Close, MenuText("matchzy.menu.close")));
        return rows;
    }

    private List<ConfigurationMenuRow> BuildPlayerConfigurationRows(int userId)
    {
        bool hasStoredPosition = savedPlayerLocationData.ContainsKey(userId);
        string storedValue = MenuText(hasStoredPosition ? "matchzy.menu.stored" : "matchzy.menu.not_stored");

        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back")),
            BooleanRow(ConfigurationMenuRowId.HumanLifeRegeneration, "matchzy.menu.regenerate_hp", humanLifeRegenerationEnabled.Contains(userId)),
            BooleanRow(ConfigurationMenuRowId.GodMode, "matchzy.menu.god_mode", humanGodModeEnabled.Contains(userId)),
            BooleanRow(ConfigurationMenuRowId.FlashProtection, "matchzy.menu.flash_protection", noFlashList.Contains(userId)),
            new(ConfigurationMenuRowId.StorePosition, MenuText("matchzy.menu.store_last_position"), storedValue)
        ];

        if (hasStoredPosition)
        {
            rows.Add(new(ConfigurationMenuRowId.TeleportPosition, MenuText("matchzy.menu.teleport_last_position")));
            rows.Add(new(ConfigurationMenuRowId.DeletePosition, MenuText("matchzy.menu.delete_last_position")));
        }

        return rows;
    }

    private List<ConfigurationMenuRow> BuildBotPositionRows(ConfigurationMenuSession session)
    {
        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back"))
        ];

        if (session.BotPositionPresetNames.Count == 0)
        {
            rows.Add(new(
                ConfigurationMenuRowId.LoadBotPosition,
                MenuText("matchzy.menu.no_bot_positions"),
                Enabled: false));
            return rows;
        }

        rows.AddRange(session.BotPositionPresetNames.Select(name => new ConfigurationMenuRow(
            ConfigurationMenuRowId.LoadBotPosition,
            name,
            BotPositionPresetName: name)));
        return rows;
    }

    private List<ConfigurationMenuRow> BuildDeleteBotPositionRows(ConfigurationMenuSession session)
    {
        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back"))
        ];

        if (session.BotPositionPresetNames.Count == 0)
        {
            rows.Add(new(
                ConfigurationMenuRowId.DeleteBotPosition,
                MenuText("matchzy.menu.no_bot_positions"),
                Enabled: false));
            return rows;
        }

        rows.AddRange(session.BotPositionPresetNames.Select(name => new ConfigurationMenuRow(
            ConfigurationMenuRowId.DeleteBotPosition,
            name,
            BotPositionPresetName: name)));
        return rows;
    }

    private List<ConfigurationMenuRow> BuildSpawnsRows(CCSPlayerController player)
    {
        bool isT = player.Team == CsTeam.Terrorist;
        bool isCt = player.Team == CsTeam.CounterTerrorist;

        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back")),
        ];

        rows.Add(spawnMarkersEnabled
            ? new(ConfigurationMenuRowId.ToggleSpawnMarkers, MenuText("matchzy.menu.hide_spawn_locations"))
            : new(ConfigurationMenuRowId.ToggleSpawnMarkers, MenuText("matchzy.menu.show_spawn_locations")));

        if (isT)
        {
            rows.Add(new(ConfigurationMenuRowId.ChangeToCT, MenuText("matchzy.menu.change_to_ct")));
        }
        else if (isCt)
        {
            rows.Add(new(ConfigurationMenuRowId.ChangeToT, MenuText("matchzy.menu.change_to_t")));
        }

        return rows;
    }

    private List<ConfigurationMenuRow> BuildUtilityRows(int userId)
    {
        bool hasHistory = lastGrenadesData.TryGetValue(userId, out List<GrenadeThrownData>? history) && history.Count > 0;
        bool HasType(string type) => nadeSpecificLastGrenadeData.TryGetValue(userId, out Dictionary<string, GrenadeThrownData>? data) && data.ContainsKey(type);

        List<ConfigurationMenuRow> rows =
        [
            new(ConfigurationMenuRowId.Back, MenuText("matchzy.menu.back")),
            new(ConfigurationMenuRowId.ClearUtilities, MenuText("matchzy.menu.clear_utilities"), Help: MenuText("matchzy.menu.help_clear")),
            new(ConfigurationMenuRowId.GlobalRethrow, MenuText("matchzy.menu.rethrow_last_util"), Help: MenuText("matchzy.menu.help_rethrow_global"))
        ];

        if (hasHistory)
        {
            rows.Insert(1, new(ConfigurationMenuRowId.RethrowUtility, MenuText("matchzy.menu.rethrow_utility"), Help: MenuText("matchzy.menu.help_rethrow_personal")));
        }
        if (HasType("smoke")) rows.Insert(rows.Count - 1, new(ConfigurationMenuRowId.RethrowSmoke, MenuText("matchzy.menu.rethrow_smoke")));
        if (HasType("flash")) rows.Insert(rows.Count - 1, new(ConfigurationMenuRowId.RethrowFlash, MenuText("matchzy.menu.rethrow_flash")));
        if (HasType("molotov")) rows.Insert(rows.Count - 1, new(ConfigurationMenuRowId.RethrowMolotov, MenuText("matchzy.menu.rethrow_molotov")));
        if (HasType("decoy")) rows.Insert(rows.Count - 1, new(ConfigurationMenuRowId.RethrowDecoy, MenuText("matchzy.menu.rethrow_decoy")));

        return rows;
    }

    private ConfigurationMenuRow BooleanRow(ConfigurationMenuRowId id, string labelKey, bool value)
    {
        return new(id, MenuText(labelKey), MenuText(value ? "matchzy.menu.on" : "matchzy.menu.off"));
    }

    private int GetSelectedRow(ConfigurationMenuSession session, int rowCount)
    {
        int selected = session.SelectedRows.GetValueOrDefault(session.Screen);
        selected = Math.Clamp(selected, 0, Math.Max(0, rowCount - 1));
        session.SelectedRows[session.Screen] = selected;
        return selected;
    }

    private void ApplyConfigurationMenuRow(ConfigurationMenuSession session, ConfigurationMenuRow row, bool right)
    {
        if (!right && row.Id is not ConfigurationMenuRowId.BotShooting and
            not ConfigurationMenuRowId.BotReactionTime and
            not ConfigurationMenuRowId.BotRespawn and
            not ConfigurationMenuRowId.BotLifeRegeneration and
            not ConfigurationMenuRowId.HumanLifeRegeneration and
            not ConfigurationMenuRowId.GodMode and
            not ConfigurationMenuRowId.FlashProtection)
        {
            return;
        }

        if (!row.Enabled)
        {
            SetConfigurationMenuFeedback(session, MenuText("matchzy.menu.unavailable"));
            return;
        }

        CCSPlayerController player = session.Player;
        bool changed = row.Id switch
        {
            ConfigurationMenuRowId.BotShooting => SetPracticeBotShooting(player, right),
            ConfigurationMenuRowId.BotReactionTime => SetPracticeBotReactionTime(player, Math.Clamp(botReactionTimeMs + (right ? 50 : -50), 0, 1000)),
            ConfigurationMenuRowId.BotRespawn => SetPracticeBotRespawn(player, right),
            ConfigurationMenuRowId.BotLifeRegeneration => SetPracticeBotLifeRegeneration(player, right),
            ConfigurationMenuRowId.HumanLifeRegeneration => SetPracticeHumanLifeRegeneration(player, right),
            ConfigurationMenuRowId.GodMode => ConfigurePracticeHumanGodMode(player, right),
            ConfigurationMenuRowId.FlashProtection => SetPracticeFlashProtection(player, right),
            ConfigurationMenuRowId.PlaceBot => ExecuteConfigurationMenuAction(() => OnBotCommand(player, null)),
            ConfigurationMenuRowId.PlaceCrouchBot => ExecuteConfigurationMenuAction(() => OnCrouchBotCommand(player, null)),
            ConfigurationMenuRowId.BoostBot => ExecuteConfigurationMenuAction(() => OnBoostBotCommand(player, null)),
            ConfigurationMenuRowId.CrouchBoostBot => ExecuteConfigurationMenuAction(() => OnCrouchBoostBotCommand(player, null)),
            ConfigurationMenuRowId.ClearAllBots => ExecuteConfigurationMenuAction(() => OnNoBotsCommand(player, null)),
            ConfigurationMenuRowId.LoadBotPosition when row.BotPositionPresetName != null =>
                ExecuteConfigurationMenuAction(() => HandleLoadBotPositionsCommand(player, row.BotPositionPresetName)),
            ConfigurationMenuRowId.DeleteBotPosition when row.BotPositionPresetName != null =>
                DeleteBotPositionFromConfigurationMenu(session, row.BotPositionPresetName),
            ConfigurationMenuRowId.StorePosition => SavePracticePlayerPosition(player),
            ConfigurationMenuRowId.TeleportPosition => LoadPracticePlayerPosition(player),
            ConfigurationMenuRowId.DeletePosition => DeletePracticePlayerPosition(player),
            ConfigurationMenuRowId.ToggleSpawnMarkers => TogglePracticeSpawnMarkers(player),
            ConfigurationMenuRowId.ChangeToT => SwitchPracticePlayerSide(player, CsTeam.Terrorist),
            ConfigurationMenuRowId.ChangeToCT => SwitchPracticePlayerSide(player, CsTeam.CounterTerrorist),
            ConfigurationMenuRowId.RethrowUtility => RethrowLastPracticeUtility(player),
            ConfigurationMenuRowId.ClearUtilities => ClearPracticeUtilities(player),
            ConfigurationMenuRowId.RethrowSmoke => RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Smoke),
            ConfigurationMenuRowId.RethrowFlash => RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Flash),
            ConfigurationMenuRowId.RethrowMolotov => RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Molotov),
            ConfigurationMenuRowId.RethrowDecoy => RethrowSpecificPracticeUtility(player, PracticeGrenadeType.Decoy),
            ConfigurationMenuRowId.GlobalRethrow => RethrowGlobalLastUtility(player),
            ConfigurationMenuRowId.StartPractice => StartPracticeFromConfigurationMenu(player),
            ConfigurationMenuRowId.StartRound => ExecuteConfigurationMenuAction(() => OnStartPracticeRoundCommand(player, null)),
            ConfigurationMenuRowId.Back => ChangeConfigurationMenuScreen(
                session,
                session.Screen is ConfigurationMenuScreen.BotPositions or ConfigurationMenuScreen.DeleteBotPositions
                    ? ConfigurationMenuScreen.BotPlacement
                    : ConfigurationMenuScreen.Root),
            ConfigurationMenuRowId.BotPlacement => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.BotPlacement),
            ConfigurationMenuRowId.ListBotPositions => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.BotPositions),
            ConfigurationMenuRowId.DeleteBotPositions => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.DeleteBotPositions),
            ConfigurationMenuRowId.BotConfiguration => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.BotConfiguration),
            ConfigurationMenuRowId.PlayerConfiguration => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.PlayerConfiguration),
            ConfigurationMenuRowId.Spawns => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.Spawns),
            ConfigurationMenuRowId.Utility => ChangeConfigurationMenuScreen(session, ConfigurationMenuScreen.Utility),
            ConfigurationMenuRowId.Close => CloseConfigurationMenuAndReport(session),
            _ => false
        };

        if (row.Id is ConfigurationMenuRowId.ChangeToT or ConfigurationMenuRowId.ChangeToCT)
        {
            if (changed) CloseConfigurationMenu(session.UserId, clearDisplay: true);
            return;
        }

        if (changed && row.Id is not ConfigurationMenuRowId.Back and
            not ConfigurationMenuRowId.BotPlacement and
            not ConfigurationMenuRowId.ListBotPositions and
            not ConfigurationMenuRowId.DeleteBotPositions and
            not ConfigurationMenuRowId.BotConfiguration and
            not ConfigurationMenuRowId.PlayerConfiguration and
            not ConfigurationMenuRowId.Spawns and
            not ConfigurationMenuRowId.Utility and
            not ConfigurationMenuRowId.StartPractice and
            not ConfigurationMenuRowId.StartRound and
            not ConfigurationMenuRowId.Close)
        {
            SetConfigurationMenuFeedback(session, MenuText("matchzy.menu.updated"));
        }
    }

    private bool ChangeConfigurationMenuScreen(ConfigurationMenuSession session, ConfigurationMenuScreen screen)
    {
        if (screen is ConfigurationMenuScreen.BotPositions or ConfigurationMenuScreen.DeleteBotPositions)
        {
            TryGetSavedBotPositionPresetNames(session.Player, out List<string> presetNames);
            session.BotPositionPresetNames = presetNames;
        }

        session.Screen = screen;
        session.SelectedRows.TryAdd(screen, 0);
        session.ScrollStarts.TryAdd(screen, 0);
        return true;
    }

    private bool DeleteBotPositionFromConfigurationMenu(ConfigurationMenuSession session, string presetName)
    {
        int presetCountBeforeDelete = session.BotPositionPresetNames.Count;
        HandleDeleteBotPositionsCommand(session.Player, presetName);

        if (!TryGetSavedBotPositionPresetNames(session.Player, out List<string> presetNames))
        {
            return false;
        }

        session.BotPositionPresetNames = presetNames;
        return presetNames.Count < presetCountBeforeDelete &&
            !presetNames.Contains(presetName, StringComparer.Ordinal);
    }

    private bool StartPracticeFromConfigurationMenu(CCSPlayerController player)
    {
        // Reuse the .prac handler so its authorization, match-state validation,
        // feedback, and practice-start side effects remain authoritative.
        OnPracCommand(player, null);
        return isPractice;
    }

    private static bool ExecuteConfigurationMenuAction(Action action)
    {
        action();
        return true;
    }

    private bool CloseConfigurationMenuAndReport(ConfigurationMenuSession session)
    {
        CloseConfigurationMenu(session.UserId, clearDisplay: true);
        return true;
    }

    private void SetConfigurationMenuFeedback(ConfigurationMenuSession session, string feedback)
    {
        session.Feedback = feedback;
        session.FeedbackExpiresUtc = DateTime.UtcNow + ConfigurationMenuFeedbackDuration;
    }

    private void UpdateConfigurationMenuHtml(ConfigurationMenuSession session)
    {
        List<ConfigurationMenuRow> rows = BuildConfigurationMenuRows(session);
        int selected = GetSelectedRow(session, rows.Count);
        int maxStart = Math.Max(0, rows.Count - ConfigurationMenuVisibleRows);
        int scrollStart = session.ScrollStarts.GetValueOrDefault(session.Screen);

        if (selected < scrollStart) scrollStart = selected;
        if (selected >= scrollStart + ConfigurationMenuVisibleRows) scrollStart = selected - ConfigurationMenuVisibleRows + 1;
        scrollStart = Math.Clamp(scrollStart, 0, maxStart);
        session.ScrollStarts[session.Screen] = scrollStart;

        StringBuilder html = new();
        html.Append("<font color='#E5B94A'><b>")
            .Append(Html(MenuText("matchzy.menu.title")))
            .Append("</b></font><br>");
        html.Append("<font color='#B9C1CC'>")
            .Append(Html(GetConfigurationMenuSubtitle(session)))
            .Append("</font><br>");

        for (int index = scrollStart; index < Math.Min(rows.Count, scrollStart + ConfigurationMenuVisibleRows); index++)
        {
            ConfigurationMenuRow row = rows[index];
            bool isSelected = index == selected;
            string color = !row.Enabled ? "#777777" : isSelected ? "#E5B94A" : "#C9D1D9";
            html.Append("<font color='").Append(color).Append("'>");
            html.Append(isSelected ? "&gt; " : "&nbsp;&nbsp;");
            html.Append(Html(row.Label));

            if (!string.IsNullOrEmpty(row.Value))
            {
                string valueColor = row.Value == MenuText("matchzy.menu.on") ? "#63D471" :
                    row.Value == MenuText("matchzy.menu.off") ? "#D06B6B" : color;
                html.Append(" : <font color='").Append(valueColor).Append("'>")
                    .Append(Html(row.Value))
                    .Append("</font>");
            }
            else if (!row.Enabled)
            {
                html.Append(" : ").Append(Html(MenuText("matchzy.menu.unavailable")));
            }

            html.Append("</font><br>");
        }

        if (rows.Count > ConfigurationMenuVisibleRows)
        {
            html.Append("<font color='#89929B'>")
                .Append(Html(string.Format(MenuText("matchzy.menu.position"), selected + 1, rows.Count)))
                .Append("</font><br>");
        }

        ConfigurationMenuRow selectedRow = rows[selected];
        string? status = GetConfigurationMenuStatus(session, selectedRow);
        if (!string.IsNullOrEmpty(status))
        {
            html.Append("<font color='#AAB4C0'>").Append(Html(status)).Append("</font><br>");
        }

        if (session.FeedbackExpiresUtc > DateTime.UtcNow && !string.IsNullOrEmpty(session.Feedback))
        {
            html.Append("<font color='#63D471'>").Append(Html(session.Feedback)).Append("</font><br>");
        }
        else
        {
            session.Feedback = null;
        }

        html.Append("<font color='#89929B'>")
            .Append(Html(MenuText("matchzy.menu.footer")))
            .Append("</font>");

        session.LastRenderedHtml = html.ToString();
    }

    private void DisplayConfigurationMenus()
    {
        if (configurationMenuSessions.Count == 0) return;

        SuppressConfigurationMenuHudAnimation();

        foreach (ConfigurationMenuSession session in configurationMenuSessions.Values.ToList())
        {
            if (string.IsNullOrEmpty(session.LastRenderedHtml)) continue;

            try
            {
                // CounterStrikeSharp's CenterHtmlMenu uses the same OnTick refresh.
                // Cache the HTML so live state is rebuilt only by the slower shared
                // timer while the HUD itself remains continuously present.
                session.Player.PrintToCenterHtml(session.LastRenderedHtml);
            }
            catch (Exception exception)
            {
                Log($"[ConfigurationMenu] Display failed for UserID {session.UserId}: {exception.Message}");
                CloseConfigurationMenu(session.UserId, clearDisplay: false);
            }
        }
    }

    private void SuppressConfigurationMenuHudAnimation()
    {
        // CS2's survival-status HUD otherwise replays a white fade roughly once
        // per second while center HTML is refreshed. Keeping GameRestart aligned
        // with its timer suppresses that client-side animation without altering
        // the menu's opacity or reducing the refreshes needed for persistence.
        configurationMenuGameRules ??= Utilities
            .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()
            ?.GameRules;

        if (configurationMenuGameRules != null)
        {
            configurationMenuGameRules.GameRestart =
                configurationMenuGameRules.RestartRoundTime < Server.CurrentTime;
        }
    }

    private string GetConfigurationMenuSubtitle(ConfigurationMenuSession session)
    {
        return session.Screen switch
        {
            ConfigurationMenuScreen.Root => MenuText("matchzy.menu.subtitle_root"),
            ConfigurationMenuScreen.BotPlacement => MenuText("matchzy.menu.subtitle_bot_placement"),
            ConfigurationMenuScreen.BotPositions => string.Format(MenuText("matchzy.menu.subtitle_bot_positions"), Server.MapName),
            ConfigurationMenuScreen.DeleteBotPositions => string.Format(MenuText("matchzy.menu.subtitle_delete_bot_positions"), Server.MapName),
            ConfigurationMenuScreen.BotConfiguration => MenuText("matchzy.menu.subtitle_bot"),
            ConfigurationMenuScreen.PlayerConfiguration => MenuText("matchzy.menu.subtitle_player"),
            ConfigurationMenuScreen.Spawns => string.Format(
                MenuText("matchzy.menu.subtitle_spawns"),
                session.Player.Team == CsTeam.Terrorist ? "T" : session.Player.Team == CsTeam.CounterTerrorist ? "CT" : MenuText("matchzy.menu.spectator")),
            ConfigurationMenuScreen.Utility => MenuText("matchzy.menu.subtitle_utility"),
            _ => string.Empty
        };
    }

    private string? GetConfigurationMenuStatus(ConfigurationMenuSession session, ConfigurationMenuRow row)
    {
        if (!string.IsNullOrEmpty(row.Help)) return row.Help;
        return null;
    }

    private string MenuText(string key) => Localizer[key];
    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
