using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    // Get5's score_ct / score_t / starting_side on the team objects of round_end and map_result:
    // rounds each team won on each side of the current map, and the side it started the map on.
    // Reset when the map goes live, counted in round_end, carried in the round backups so a
    // restore (also on another server) puts them back to the restored round.
    public partial class MatchZy
    {
        private record struct SideScores(int Team1Ct, int Team1T, int Team2Ct, int Team2T, string? Team1StartingSide);

        // Team1StartingSide: "CT" / "TERRORIST", null when not known (a restore of a backup from
        // before these were saved).
        private SideScores sideScores;

        private void ResetSideScores()
        {
            sideScores = new SideScores(0, 0, 0, 0, teamSides.GetValueOrDefault(matchzyTeam1));
        }

        // Called before any halftime swap, so reverseTeamSides still maps the sides of this round.
        // A team is only credited while its count is below its engine score, so a round end the
        // engine did not score (a restart, a backup load) cannot push the counts past the score.
        private void CountSideScore(int winner, int t1score, int t2score)
        {
            string? side = winner == (int)CsTeam.CounterTerrorist ? "CT"
                : winner == (int)CsTeam.Terrorist ? "TERRORIST"
                : null;
            if (side == null)
                return;

            var s = sideScores;
            bool isCt = side == "CT";
            if (reverseTeamSides[side] == matchzyTeam1)
            {
                if (s.Team1Ct + s.Team1T >= t1score) return;
                sideScores = isCt ? s with { Team1Ct = s.Team1Ct + 1 } : s with { Team1T = s.Team1T + 1 };
            }
            else
            {
                if (s.Team2Ct + s.Team2T >= t2score) return;
                sideScores = isCt ? s with { Team2Ct = s.Team2Ct + 1 } : s with { Team2T = s.Team2T + 1 };
            }
        }

        private MatchZyStatsTeam BuildStatsTeam(Team team, int score, List<StatsPlayer> players)
        {
            bool isTeam1 = team == matchzyTeam1;
            string? currentSide = teamSides.GetValueOrDefault(team);
            int ct = isTeam1 ? sideScores.Team1Ct : sideScores.Team2Ct;
            int t = isTeam1 ? sideScores.Team1T : sideScores.Team2T;

            // map_result is sent from cs_win_panel_match, which can run before the round_end that
            // counts the deciding round: that round was won on the side the team is on now.
            if (ct + t == score - 1)
            {
                if (currentSide == "CT") ct++;
                else if (currentSide == "TERRORIST") t++;
            }
            // Counts that do not add up to the score (a restore of an older backup, a failed load,
            // a plugin reload mid-map) are unknown: report 0 / 0 as before rather than wrong numbers.
            if (ct + t != score)
                ct = t = 0;

            string? startingSide = sideScores.Team1StartingSide == null ? null
                : isTeam1 ? sideScores.Team1StartingSide
                : sideScores.Team1StartingSide == "CT" ? "TERRORIST" : "CT";
            return new MatchZyStatsTeam(team.id, team.teamName, team.seriesScore, score, ct, t, players)
            {
                Side = Get5Side(SideToTeamNum(currentSide)),
                StartingSide = Get5Side(SideToTeamNum(startingSide)),
            };
        }

        private static int SideToTeamNum(string? side) => side switch
        {
            "CT" => (int)CsTeam.CounterTerrorist,
            "TERRORIST" => (int)CsTeam.Terrorist,
            _ => (int)CsTeam.None,
        };

        private void AddSideScoresToBackup(Dictionary<string, string> roundData)
        {
            roundData["team1_score_ct"] = sideScores.Team1Ct.ToString();
            roundData["team1_score_t"] = sideScores.Team1T.ToString();
            roundData["team2_score_ct"] = sideScores.Team2Ct.ToString();
            roundData["team2_score_t"] = sideScores.Team2T.ToString();
            roundData["team1_starting_side"] = sideScores.Team1StartingSide ?? "";
        }

        // null when the backup has no side scores (made before they were saved).
        private static SideScores? ReadSideScoresFromBackup(Dictionary<string, string> backupData)
        {
            if (backupData.TryGetValue("team1_score_ct", out var a) && int.TryParse(a, out int t1Ct)
                && backupData.TryGetValue("team1_score_t", out var b) && int.TryParse(b, out int t1T)
                && backupData.TryGetValue("team2_score_ct", out var c) && int.TryParse(c, out int t2Ct)
                && backupData.TryGetValue("team2_score_t", out var d) && int.TryParse(d, out int t2T))
            {
                string? start = backupData.GetValueOrDefault("team1_starting_side");
                return new SideScores(Math.Max(0, t1Ct), Math.Max(0, t1T), Math.Max(0, t2Ct), Math.Max(0, t2T),
                    start == "CT" || start == "TERRORIST" ? start : null);
            }
            return null;
        }

        // sameMap: the backup is of the map that is running, so the starting side known since it
        // went live stays valid even when the backup does not carry one.
        private void ApplySideScores(SideScores? restored, bool sameMap)
        {
            if (restored is SideScores s)
            {
                sideScores = s;
                return;
            }
            // Older backup: the counts of the restored round are unknown. Zero them (BuildStatsTeam
            // then reports 0 / 0 until the map ends, as the counts no longer add up to the score).
            sideScores = new SideScores(0, 0, 0, 0, sameMap ? sideScores.Team1StartingSide : null);
        }
    }
}
