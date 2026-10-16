using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace MatchZy
{
    public partial class MatchZy
    {
        // The engine execs gamemode_<mode>_server.cfg on EVERY level load, right after
        // gamemode_<mode>.cfg and before any plugin code runs. If the host's cfgs (server.cfg,
        // gamemode_*.cfg) restore a non-zero bot_quota, the engine starts adding bots while the
        // players of a series are still reconnecting after the changelevel. warmup.cfg removes
        // them, but only after the map has loaded: too late. With a tight -maxplayers the
        // reconnecting humans + CSTV + the first bot fill every slot and the next bot add
        // segfaults the server. Pinning bot_quota 0 in the _server.cfg closes that window.
        // MatchZy's own bot features (warmup bots, bot team, practice bots) set bot_quota
        // themselves after the level is up, so they are not affected.
        private static readonly string[] LevelLoadServerCfgs =
        {
            "gamemode_competitive_server.cfg",
            "gamemode_competitive2v2_server.cfg",
        };

        private static readonly Regex BotQuotaLine = new(@"^\s*bot_quota\s+""?([^""\s]+)", RegexOptions.IgnoreCase);

        private const string LevelLoadBotQuotaBlock =
            "// Added by MatchZy: keep the engine from adding bots during a level load, while the\n" +
            "// players of a series are still reconnecting (a full server + bot add can crash it).\n" +
            "// MatchZy sets bot_quota itself when it needs bots.\n" +
            "bot_quota 0\n";

        /// <summary>
        /// Makes sure every level-load _server.cfg pins bot_quota 0. A missing file is created, a
        /// file without any bot_quota line gets the line appended. A bot_quota the admin set to
        /// another value is left alone and only logged. Never fatal.
        /// </summary>
        private void EnsureLevelLoadBotQuota()
        {
            string cfgDir;
            try
            {
                cfgDir = Path.Join(Server.GameDirectory, "csgo", "cfg");
            }
            catch (Exception ex)
            {
                Log($"[LevelLoadBotQuota] {ex.GetType().Name}: {ex.Message}");
                return;
            }

            foreach (string fileName in LevelLoadServerCfgs)
            {
                string path = Path.Join(cfgDir, fileName);
                try
                {
                    if (!File.Exists(path))
                    {
                        File.WriteAllText(path, LevelLoadBotQuotaBlock);
                        Log($"[LevelLoadBotQuota] Created csgo/cfg/{fileName} with bot_quota 0.");
                        continue;
                    }

                    // The engine runs the file top to bottom, so the last bot_quota line wins.
                    string? quota = null;
                    foreach (string rawLine in File.ReadLines(path))
                    {
                        int comment = rawLine.IndexOf("//", StringComparison.Ordinal);
                        string line = comment >= 0 ? rawLine[..comment] : rawLine;
                        Match m = BotQuotaLine.Match(line);
                        if (m.Success) quota = m.Groups[1].Value;
                    }

                    if (quota == null)
                    {
                        string existing = File.ReadAllText(path);
                        string separator = existing.Length == 0 || existing.EndsWith('\n') ? "\n" : "\n\n";
                        File.AppendAllText(path, separator + LevelLoadBotQuotaBlock);
                        Log($"[LevelLoadBotQuota] Appended bot_quota 0 to csgo/cfg/{fileName}.");
                    }
                    else if (quota != "0")
                    {
                        Log($"[LevelLoadBotQuota] WARNING: csgo/cfg/{fileName} sets bot_quota {quota}. The engine adds bots on every level load, while series players are still reconnecting; with a full server this can crash it. Set bot_quota 0 there unless you need the bots.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[LevelLoadBotQuota] {fileName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
