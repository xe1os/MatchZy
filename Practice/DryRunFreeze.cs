using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

namespace MatchZy
{
    // Dryrun freeze time:
    //   .dryrun [0-60]  - start (or restart) a dryrun; with a number that is the freeze time in
    //                     seconds instead of dryrun.cfg's 6.
    //   .rr [0-60]      - in a dryrun: restart the round, optionally with a new freeze time. The
    //                     freeze time stays until it is changed again or the dryrun ends.
    public partial class MatchZy
    {
        private const int MaxDryRunFreezeSeconds = 60;

        // "" -> null (keep the current freeze time); "0".."60" -> that; anything else -> false.
        private static bool TryParseDryRunFreeze(string? arg, out int? seconds)
        {
            seconds = null;
            string a = (arg ?? "").Trim();
            if (a.Length == 0)
                return true;
            if (int.TryParse(a, out int s) && s >= 0 && s <= MaxDryRunFreezeSeconds)
            {
                seconds = s;
                return true;
            }
            return false;
        }

        private static void SetDryRunFreezeTime(int seconds)
            => Server.ExecuteCommand($"mp_freezetime {seconds}");

        // .rr / !rr during a dryrun. Restarting the game for everyone: same admins as .dryrun.
        private void HandleDryRunRestartRound(CCSPlayerController? player, string arg)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (!TryParseDryRunFreeze(arg, out int? seconds))
            {
                ReplyToUserCommand(player, Localizer.ForPlayer(player, "matchzy.cc.usage", ".rr [0-60]"));
                return;
            }
            if (seconds is int s)
                SetDryRunFreezeTime(s);
            Server.ExecuteCommand("mp_restartgame 1");
            ScheduleRoundHistoryWipe();
            if (seconds is int set)
                PrintLocalizedToAll("matchzy.prac.dryrunrestartedfreeze", set);
            else
                PrintLocalizedToAll("matchzy.prac.dryrunrestarted");
        }
    }
}
