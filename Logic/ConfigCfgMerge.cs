using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MatchZy
{
    // The config.cfg update merge (see ConfigManager.MergeMissingConfigCvars), kept free of
    // CounterStrikeSharp and file I/O so it can be unit tested (tests/MatchZy.Tests).
    public static class ConfigCfgMerge
    {
        public const string UpdateHeader = "// --- Added by MatchZy update (missing cvars appended) ---";

        // Settings that were renamed or removed and are pruned from old files even when the
        // registered-name scan is not available.
        public static readonly IReadOnlyCollection<string> RetiredCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "matchzy_ready_hint_suppress_warmup", // renamed -> matchzy_ready_hide_warmup_hud (itself now retired)
            "matchzy_ready_hide_warmup_hud",      // folded into matchzy_ready_hint_style modes 1/2
            "matchzy_ready_block_warmup_announce",// removed: experimental warmup-announce block (dead end)
            "matchzy_random_spawns",              // removed with the old random-spawn option
        };

        public sealed class Result
        {
            // The new file content, or null when the file stays untouched.
            public string? NewContent { get; init; }
            public int AppendedCount { get; init; }
            public bool CollapsedDuplicateHeaders { get; init; }
            public IReadOnlyCollection<string> RemovedNames { get; init; } = Array.Empty<string>();
            // registeredNames was given but did not cover every template cvar, so only RetiredCvars were pruned.
            public bool PruneSkipped { get; init; }
        }

        /// <summary>
        /// Merges the config.cfg template into an existing config.cfg: appends the comment+setting
        /// blocks whose cvar is not in the file (active or commented out), under one update header,
        /// collapses duplicate update headers left by older builds, and removes active matchzy_*
        /// lines of retired settings. Admin values and every comment line are kept.
        /// </summary>
        public static Result Merge(string existing, string templateContent, ISet<string>? registeredNames)
        {
            var existingCvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in existing.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("//"))
                {
                    // Treat a commented-out cvar (e.g. "// matchzy_x true") as present so we do not
                    // re-append something the admin deliberately disabled. Only when the cvar name is
                    // the FIRST token after //, not a passing mention in prose
                    // (e.g. "// Example matchzy_hostname_format ...").
                    var commentedName = FirstToken(line.TrimStart('/').Trim());
                    if (commentedName.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase))
                        existingCvars.Add(commentedName);
                    continue;
                }
                var name = FirstToken(line);
                if (name.Length > 0)
                    existingCvars.Add(name);
            }

            var missingBlocks = new List<string>();
            var currentBlock = new List<string>();

            void FlushBlock()
            {
                if (currentBlock.Count == 0)
                    return;
                string? cvarName = null;
                foreach (var bl in currentBlock)
                {
                    var t = bl.Trim();
                    if (t.Length == 0 || t.StartsWith("//"))
                        continue;
                    cvarName = FirstToken(t);
                    break;
                }
                if (cvarName != null && !existingCvars.Contains(cvarName))
                    missingBlocks.Add(string.Join("\n", currentBlock));
                currentBlock.Clear();
            }

            foreach (var rawLine in templateContent.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    FlushBlock();
                else
                    currentBlock.Add(line);
            }
            FlushBlock();

            // Collapse duplicate update headers left by older builds, which appended a FRESH header
            // on every load. Keep the first; removing the rest only drops redundant comment lines.
            var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
            int firstHeaderIdx = lines.FindIndex(l => l.Trim() == UpdateHeader);
            bool removedDuplicateHeaders = false;
            if (firstHeaderIdx >= 0)
            {
                for (int i = lines.Count - 1; i > firstHeaderIdx; i--)
                {
                    if (lines[i].Trim() == UpdateHeader)
                    {
                        lines.RemoveAt(i);
                        removedDuplicateHeaders = true;
                    }
                }
            }

            // Automatic pruning: a matchzy_* name that the plugin no longer registers (no FakeConVar,
            // no [ConsoleCommand]) and that is not in the template was removed from the source, so it
            // is pruned like RetiredCvars without having to be listed. Only trusted when every
            // template cvar resolved to a registered name - if the reflection scan came back
            // incomplete, it would otherwise delete live settings.
            var templateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in templateContent.Split('\n'))
            {
                var t = rawLine.Trim();
                if (t.Length == 0)
                    continue;
                string first = t.StartsWith("//") ? FirstToken(t.TrimStart('/').Trim()) : FirstToken(t);
                if (first.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase))
                    templateNames.Add(first);
            }
            bool autoPrune = registeredNames != null && registeredNames.Count > 0 && templateNames.All(registeredNames.Contains);
            bool IsRetired(string name) =>
                RetiredCvars.Contains(name)
                || (autoPrune
                    && name.StartsWith("matchzy_", StringComparison.OrdinalIgnoreCase)
                    && !templateNames.Contains(name)
                    && !registeredNames!.Contains(name));

            // Remove the active setting line only. Comment lines are never touched: help text, the
            // admin's own notes and commented-out settings are harmless (only an active line prints
            // "Unknown command"), and help text often spans several lines.
            var removedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("//"))
                    continue;
                string first = FirstToken(t);
                if (!IsRetired(first))
                    continue;
                removedNames.Add(first);
                lines.RemoveAt(i);
            }

            var result = new Result
            {
                AppendedCount = missingBlocks.Count,
                CollapsedDuplicateHeaders = removedDuplicateHeaders,
                RemovedNames = removedNames,
                PruneSkipped = registeredNames != null && !autoPrune,
            };

            // Nothing new to add and nothing to clean -> leave the file untouched.
            if (missingBlocks.Count == 0 && !removedDuplicateHeaders && removedNames.Count == 0)
                return result;

            var sb = new StringBuilder(string.Join("\n", lines).TrimEnd('\n'));
            sb.Append('\n');
            if (missingBlocks.Count > 0)
            {
                sb.Append('\n');
                // Add the header only when the file has none (after the dedup above).
                if (firstHeaderIdx < 0)
                    sb.Append(UpdateHeader).Append('\n');
                foreach (var block in missingBlocks)
                {
                    sb.Append(block);
                    sb.Append("\n\n");
                }
            }

            return new Result
            {
                NewContent = sb.ToString(),
                AppendedCount = result.AppendedCount,
                CollapsedDuplicateHeaders = result.CollapsedDuplicateHeaders,
                RemovedNames = result.RemovedNames,
                PruneSkipped = result.PruneSkipped,
            };
        }

        private static string FirstToken(string line) => line.Split(new[] { ' ', '\t' }, 2)[0];
    }
}
