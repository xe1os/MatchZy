using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Menu;

namespace MatchZy
{
    // .strats - WasdMenu over the saved strategies of this map (StrategyRecorder.cs): start one,
    // record or clear its slots, delete it. Menu types stay inside the OpenX methods and the entry
    // point goes through OpenMenuGuarded, so CS2MenuManager stays optional (see AdminMenu).
    public partial class MatchZy
    {
        [ConsoleCommand("css_strats", "Opens the strategy menu")]
        public void OnStratsMenuCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!RequirePractice(player) || !IsHumanPlayerValid(player))
                return;
            // Defer: opening a menu on the chat-dispatch tick gets clobbered by the chat broadcast.
            Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(player))
                    OpenMenuGuarded(player!, OpenStratsMenu);
            });
        }

        private void OpenStratsMenu(CCSPlayerController player)
        {
            var strats = LoadSavedStrats();
            var menu = new WasdMenu($"Strategies ({strats.Count})", this);
            if (strats.Count == 0)
                menu.AddItem("(none - .createstrat <name>)", (_, _) => { });
            foreach (var (name, saved) in strats.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                string n = name;
                string side = saved.Team == (int)CsTeam.CounterTerrorist ? "CT" : "T";
                menu.AddItem($"{n} ({side}, {saved.Roles.Count}/{MaxStratSlots})", (p, _) => OpenStratMenu(p, n, menu));
            }
            menu.Display(player, 0);
        }

        private void OpenStratMenu(CCSPlayerController player, string name, WasdMenu prev)
        {
            if (!LoadSavedStrats().TryGetValue(name, out var saved))
                return;
            var sub = new WasdMenu(name, this) { PrevMenu = prev };
            sub.AddItem("Start replay", (p, _) => Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(p))
                    HandleStratCommand(p, name);
            }));
            for (int slot = 1; slot <= MaxStratSlots; slot++)
            {
                int s = slot;
                var role = saved.Roles.FirstOrDefault(r => r.Slot == s);
                string label = role == null
                    ? $"Slot {s}: empty"
                    : $"Slot {s}: {role.Name} ({(role.Path.Frames.Count / 64.0f):0.0} s)";
                sub.AddItem(label, (p, _) => OpenStratSlotMenu(p, name, s, role != null, sub));
            }
            sub.AddItem("Stop replay", (p, _) => Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(p))
                    HandleStratCommand(p, "stop");
            }));
            sub.AddItem("Delete strategy", (p, _) => Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(p))
                    HandleDelStratCommand(p, name);
            }));
            sub.Display(player, 0);
        }

        private void OpenStratSlotMenu(CCSPlayerController player, string name, int slot, bool recorded, WasdMenu prev)
        {
            var sub = new WasdMenu($"{name}: slot {slot}", this) { PrevMenu = prev };
            sub.AddItem(recorded ? "Record again" : "Record", (p, _) => Server.NextFrame(() =>
            {
                if (IsHumanPlayerValid(p))
                    StartSlotRecording(p, name, slot);
            }));
            if (recorded)
            {
                sub.AddItem("Play it myself (others as bots)", (p, _) => Server.NextFrame(() =>
                {
                    if (IsHumanPlayerValid(p))
                        HandleStratCommand(p, $"{name} me {slot}");
                }));
                sub.AddItem("Clear slot", (p, _) => Server.NextFrame(() =>
                {
                    if (IsHumanPlayerValid(p))
                        ClearStratSlot(p, name, slot);
                }));
            }
            sub.Display(player, 0);
        }
    }
}
