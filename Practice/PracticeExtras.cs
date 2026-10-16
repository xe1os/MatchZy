using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace MatchZy
{
    // Wiring for the practice extras: bot combat (BotCombat.cs), health regeneration
    // (PracticeHealth.cs), respawning (PracticeRespawn.cs), .startround (StartRound.cs) and bot spawn
    // pools (BotSpawnPools.cs).
    public partial class MatchZy
    {
        // Called once from Load.
        private void RegisterPracticeExtras()
        {
            RegisterListener<Listeners.OnTick>(OnBotCombatTick);
            RegisterListener<Listeners.OnTick>(OnPathReplayTick);
            RegisterListener<Listeners.OnPlayerButtonsChanged>(OnBotSpawnMarkerButtonHandler);
            RegisterEventHandler<EventPlayerDeath>(OnPracticeDeathPre, HookMode.Pre);
            RegisterEventHandler<EventPlayerSpawn>(OnPracticeSpawnPost);
        }

        // Practice start and end: every extra goes back to its default, so a new session never
        // inherits shooting bots, a frozen .startround or regeneration settings.
        private void ResetPracticeExtras()
        {
            ResetBotCombatState();
            botShootEnabled = false;
            botReactionTimeMs = 500;
            _botJiggleRandom = false;
            RerollBotJiggle();
            ResetPracticeHealth();
            ResetPracticeRespawn();
            ResetStartRound();
            ClearBotSpawnMarkers();
            ResetPathReplay();
        }

        // Chat dispatch for the extras (both the bare and the argument forms). Returns true when the
        // command word was one of these.
        private bool HandlePracticeExtraChatCommand(CCSPlayerController player, string cmdWord, string arg)
        {
            switch (cmdWord)
            {
                case ".botshoot": HandleBotShootCommand(player, arg); return true;
                case ".botreactiontime": HandleBotReactionTimeCommand(player, arg); return true;
                case ".botrespawn": HandleBotRespawnCommand(player, arg); return true;
                case ".botlifereg": HandleBotLifeRegCommand(player, arg); return true;
                case ".liferegon": HandleLifeRegenCommand(player, arg); return true;
                case ".allliferegon": HandleAllLifeRegenCommand(player, arg); return true;
                case ".botjigglerange": HandleBotJiggleRangeCommand(player, arg); return true;
                case ".botjigglerandom": HandleBotJiggleRandomCommand(player, arg); return true;
                case ".startround": OnStartRoundCommand(player, null); return true;
                case ".rec": HandleRecCommand(player, arg); return true;
                case ".replay": HandleReplayCommand(player, arg); return true;
                case ".saverec": HandleSaveRecCommand(player, arg); return true;
                case ".loadrec": HandleLoadRecCommand(player, arg); return true;
                case ".listrec": OnListRecConsoleCommand(player, null); return true;
                case ".delrec": HandleDelRecCommand(player, arg); return true;
                case ".showrec": OnShowRecConsoleCommand(player, null); return true;
                case ".botspawn": HandleBotSpawnCommand(player, arg); return true;
                case ".delbotspawn": HandleDelBotSpawnCommand(player, arg); return true;
                case ".listbotspawn": OnListBotSpawnCommand(player, null); return true;
                case ".placebot": HandlePlaceBotCommand(player, arg, keepExisting: false); return true;
                case ".placenewbot": HandlePlaceBotCommand(player, arg, keepExisting: true); return true;
                case ".kicklastbot": OnKickLastBotCommand(player, null); return true;
                case ".showbotspawn": OnShowBotSpawnCommand(player, null); return true;
                case ".slp": OnSlpCommand(player, null); return true;
                case ".tlp": OnTlpCommand(player, null); return true;
                case ".dlp": OnDlpCommand(player, null); return true;
            }
            return false;
        }
    }
}
