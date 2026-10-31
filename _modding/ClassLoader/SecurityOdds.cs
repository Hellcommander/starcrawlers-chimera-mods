using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Spells out how disarm, hack and lockpick chances are worked out, on the event screens (and in the log for
    /// hacks, which roll instantly). Lockpicking also gets its % on the button, which the game never shows.
    /// Optionally the breakdown is "Corp Intel" you buy once per run for credits.
    /// </summary>
    internal static class SecurityOdds
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> IntelCost;
        internal static ConfigEntry<KeyCode> IntelKey;
        private const string IntelTag = "SCCL_CORP_INTEL";

        private static string disarmBase, lockBase; // the game's own text, so the breakdown can be redrawn after buying intel

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("SecurityOdds", "ShowBreakdown", true, "Explain trap, hack and lockpick chances on the event screens.");
            IntelCost = cfg.Bind("SecurityOdds", "CorpIntelCost", 0, "0 = always shown. Above 0: the breakdown is Corp Intel you buy once per run for this many credits.");
            IntelKey = cfg.Bind("SecurityOdds", "CorpIntelKey", KeyCode.I, "Key that buys Corp Intel while a trap or lock screen is open.");
        }

        private static bool Unlocked => IntelCost.Value <= 0 || (Player.instance != null && Player.instance.HasTag(IntelTag));

        private static string Locked => $"<color=#9FB7C9>Corp Intel explains these odds for this run: {IntelCost.Value} credits ({IntelKey.Value}).</color>";

        private static string Small(string s) => "\n<size=70%>" + s + "</size>";

        private static string Signed(int v) => v >= 0 ? "+ " + v : "− " + (-v);

        // ---- formulas (mirroring Menu_Event and Hazard) --------------------------------------------------------

        private static int DifficultyTerm() => Game.DIFFICULTY switch { 0 => 10, 2 => -10, 3 => -20, _ => 0 };

        internal static string Disarm(Hazard h, bool hack, out int total)
        {
            Player p = Player.instance;
            int baseChance = h.GetDisarmBaseChance(); // also fixes the hazard's random "luck" roll if it isn't set yet
            var parts = new List<string> { $"trap {h.disarmBaseChance}" };
            if (DifficultyTerm() != 0) parts.Add($"{Signed(DifficultyTerm())} difficulty");
            if (h.disarmFloorMod != 0 && Game.FLOOR != 0) parts.Add($"{Signed(-h.disarmFloorMod * Game.FLOOR)} floor depth");
            if (h.hazardFlag_A != 0) parts.Add($"{Signed(h.hazardFlag_A)} luck");
            total = baseChance;
            foreach (StatObj m in h.attributeMods ?? new StatObj[0])
            {
                if (m == null || m.amount <= 0) continue;
                if (hack && m.GetStatType() != STAT_TYPE.Smarts) continue;
                int div = hack ? m.amount / 2 : m.amount;
                if (div <= 0) continue;
                int stat = p.GetStat(m.statType);
                total += stat / div;
                parts.Add($"+ {m.statType} {stat}÷{div} ({stat / div})");
            }
            if (Player_AbilityControl.instance.HasAbility("PA_PASSIVE_Security_Wirecutter"))
            {
                int w = Math.Max(0, Player_AbilityControl.instance.GetAbility("PA_PASSIVE_Security_Wirecutter").CalculateHitChance(null));
                total += w;
                parts.Add($"+ Wirecutter {w}");
            }
            if (hack)
            {
                int hk = 5 * (1 + Player_SkillControl.instance.GetRank("SKILLTREE_Hacker"));
                total += hk;
                parts.Add($"+ Hacker skill {hk}");
            }
            int raw = total;
            total = Mathf.Clamp(total, 0, 100);
            return $"{total}% = {string.Join(" ", parts)}{(raw != total ? $" (= {raw}, kept within 0–100)" : "")}.";
        }

        internal static string Lockpick(int lockMod, out int total)
        {
            Player p = Player.instance;
            int d = Math.Max(1, Game.DIFFICULTY);
            int sav = p.GetStat(STAT_TYPE.Savvy), rec = p.GetStat(STAT_TYPE.Recognition);
            var parts = new List<string> { "40 base" };
            if (lockMod != 0) parts.Add($"{Signed(lockMod)} lock");
            parts.Add($"+ Savvy {sav}{(d > 1 ? $"÷{d} ({sav / d})" : "")}");
            parts.Add($"+ Recognition {rec}{(d > 1 ? $"÷{d} ({rec / d})" : "")}");
            total = Mathf.Clamp(40 + lockMod + sav / d + rec / d, 0, 100);
            if (Player_AbilityControl.instance.HasAbility("PA_PASSIVE_Security_Lockpopper"))
            {
                int lp = Math.Max(0, Player_AbilityControl.instance.GetAbility("PA_PASSIVE_Security_Lockpopper").CalculateHitChance(null));
                total += lp;
                parts.Add($"+ Lockpopper {lp}");
            }
            return $"{total}% = {string.Join(" ", parts)}{(d > 1 ? " (stats ÷ difficulty)" : "")}";
        }

        private static int? CurrentLockMod(Menu_Event ev)
        {
            var t = Traverse.Create(ev);
            Door door = t.Field("lockpickDoor").GetValue<Door>();
            if (door != null) return door.lockpickMod;
            InteractiveObject io = t.Field("lockpickObject").GetValue<InteractiveObject>();
            return io != null ? io.lockpickMod : (int?)null;
        }

        // ---- patches ------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Menu_Event), nameof(Menu_Event.StartDisarmEvent))]
        private static class Patch_Disarm
        {
            private static void Postfix(Menu_Event __instance, Hazard hazard)
            {
                if (!Enabled.Value || hazard == null || !Traverse.Create(__instance).Field("isDisarming").GetValue<bool>()) return;
                disarmBase = __instance.disarmText.text;
                Redraw(__instance);
            }
        }

        [HarmonyPatch(typeof(Menu_Event), nameof(Menu_Event.HackHazard))]
        private static class Patch_Hack
        {
            private static void Prefix(Hazard hazard)
            {
                if (!Enabled.Value || !Unlocked || hazard == null || hazard.GetTripped()) return;
                try { Game.LOG("<color=#9FB7C9>Hack odds: " + Disarm(hazard, true, out _) + "</color>"); }
                catch (Exception e) { Plugin.Log.LogError("Security odds: " + e); }
            }
        }

        /// <summary>The game only shows how many picks you have; add the chance.</summary>
        [HarmonyPatch(typeof(Menu_Event), "LockpickCheck")]
        private static class Patch_LockButton
        {
            private static void Postfix(Menu_Event __instance, bool __result)
            {
                if (!Enabled.Value || !__result || !Unlocked) return;
                int? mod = CurrentLockMod(__instance);
                if (mod == null) return;
                Lockpick(mod.Value, out int pct);
                __instance.lockpickBtnText.text += $" – {pct}%";
            }
        }

        [HarmonyPatch]
        private static class Patch_LockScreen
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new System.Reflection.MethodBase[]
            {
                AccessTools.Method(typeof(Menu_Event), nameof(Menu_Event.StartLockPickEvent), new[] { typeof(Door) }),
                AccessTools.Method(typeof(Menu_Event), nameof(Menu_Event.StartLockPickEvent), new[] { typeof(InteractiveObject), typeof(bool), typeof(string) }),
            };

            private static void Postfix(Menu_Event __instance)
            {
                if (!Enabled.Value || !Traverse.Create(__instance).Field("isLockpicking").GetValue<bool>()) return;
                lockBase = __instance.lockpickText.text;
                Redraw(__instance);
            }
        }

        private static void Redraw(Menu_Event ev)
        {
            try
            {
                if (Traverse.Create(ev).Field("isDisarming").GetValue<bool>() && disarmBase != null)
                {
                    Hazard h = Traverse.Create(ev).Field("disarmHazard").GetValue<Hazard>();
                    ev.disarmText.text = disarmBase + Small(Unlocked ? "Your odds: " + Disarm(h, false, out _) : Locked);
                    ev.disarmText.ForceMeshUpdate();
                    ev.disarmText.rectTransform.sizeDelta = ev.disarmText.GetPreferredValues();
                }
                if (Traverse.Create(ev).Field("isLockpicking").GetValue<bool>() && lockBase != null)
                {
                    int? mod = CurrentLockMod(ev);
                    if (mod != null && ev.lockpickBtn.gameObject.activeSelf)
                        ev.lockpickText.text = lockBase + Small(Unlocked ? "Odds: " + Lockpick(mod.Value, out _) : Locked);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Security odds: " + e); }
        }

        /// <summary>Buying Corp Intel from an open trap or lock screen.</summary>
        [HarmonyPatch(typeof(Menu_Event), "Update")]
        private static class Patch_Buy
        {
            private static void Postfix(Menu_Event __instance)
            {
                if (Enabled.Value && !Unlocked && Input.GetKeyDown(IntelKey.Value)) TryBuy(__instance);
            }
        }

        internal static void TryBuy(Menu_Event __instance)
        {
            {
                var t = Traverse.Create(__instance);
                if (!t.Field("isDisarming").GetValue<bool>() && !t.Field("isLockpicking").GetValue<bool>()) return;
                if (Menu_Inventory.instance.GetCredits() < IntelCost.Value)
                {
                    Game.LOG("<color=#FF7272>Not enough credits for Corp Intel.</color>");
                    AudioManager.PlayEvent("event:/UI/UI_Error");
                    return;
                }
                Menu_Inventory.instance.AdjustCredits(-IntelCost.Value);
                Player.instance.AddTag(IntelTag, 1);
                AudioManager.PlayEvent("event:/UI/UI_CashRegister");
                Game.LOG($"Corp Intel bought for {IntelCost.Value} credits: security odds are explained for the rest of this run.");
                if (t.Field("isLockpicking").GetValue<bool>()) Traverse.Create(__instance).Method("LockpickCheck").GetValue();
                Redraw(__instance);
            }
        }
    }
}
