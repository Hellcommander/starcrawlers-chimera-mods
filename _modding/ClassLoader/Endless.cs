using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// The Ichor Deep: endless content for a character who beat Cerberus and chose to stay (see WinCredits). A breach
    /// opens in the Cerberus arena; it's a side area of the "ichordeep" kind (Mods/SideAreas), but it never collapses
    /// and every layer has a rift one depth further down, with no bottom. Depth 1 starts at final-floor strength;
    /// each depth adds <c>PowerPerDepth</c> to the enemies' stats (damage included), <c>HealthPerDepth</c> to their
    /// health, a little elite chance, and more and better loot. Every layer is fresh: a generated place or an echo of
    /// any floor, its own roster and anomalies. Any way out leads back to the arena; next time you can start at
    /// depth 1 or at the deepest depth you've reached.
    ///
    /// State: the win and the deepest depth are player tags (they travel with the save, and a save without the mod
    /// simply ignores them); the rest is the side areas' own saved state.
    /// </summary>
    internal static class Endless
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PowerPerDepth, HealthPerDepth;

        internal const string KindId = "ichordeep";
        internal const string WonTag = "SCCL_CERBERUS_DOWN", BestTag = "SCCL_DEEP_BEST", ScaledTag = "SCCLD_SCALED";
        internal const string Color = "#9BFF5C";
        private const int Origin = 7; // 7_TheEnd's floor index

        private static int startDepth = 1;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Gameplay", "EndlessDeep", true,
                "After beating Cerberus you can stay: a breach in the arena opens the Ichor Deep, an endless descent that gets harder and richer with every depth.");
            PowerPerDepth = cfg.Bind("Gameplay", "EndlessPowerPerDepth", 15, new ConfigDescription(
                "Percent added to Ichor Deep enemies' stats (damage, accuracy, armour...) per depth, on top of final-floor strength.", new AcceptableValueRange<int>(0, 100)));
            HealthPerDepth = cfg.Bind("Gameplay", "EndlessHealthPerDepth", 20, new ConfigDescription(
                "Percent added to Ichor Deep enemies' health per depth, on top of final-floor strength.", new AcceptableValueRange<int>(0, 200)));
        }

        internal static bool Won => Player.instance != null && Player.instance.HasTag(WonTag);
        internal static int Best => Player.instance != null ? Player.instance.GetTag(BestTag) : 0;
        internal static bool Available => Enabled.Value && SideAreaData.Kinds.Any(k => string.Equals(k.id, KindId, StringComparison.OrdinalIgnoreCase));

        /// <summary>This floor's breach leads into the Deep.</summary>
        internal static bool Is(int floor) => floor == Origin && Won && SideKinds.KindOf(floor).id == KindId;

        /// <summary>You're in the Deep right now.</summary>
        internal static bool Here => SideAreas.InSide && SideKinds.Current.id == KindId;

        internal static int Depth => Here ? SideAreas.Depth : 0;

        // ---- after the credits: opening it ------------------------------------------------------------------------

        /// <summary>The run goes on: the end-game locks come off and the breach opens beside you.</summary>
        internal static void Unlock()
        {
            Player p = Player.instance;
            if (p == null) return;
            p.SetTag(WonTag, 1);
            // vanilla keeps you "in combat" for as long as this is set; the truth pool can't restart the fight (THEEND_TRUTHPOOL stays)
            p.RemoveTag("FINALFIGHT");
            p.RemoveTag("IMMORTAL");
            Game.instance.resolveTimeMax = 1f; // Cerberus's death raised it to 600 s
            Settle();
            if (!Available) { Game.LOG("<color=#9FB7C9>The way ahead is yours. (The Ichor Deep needs the Breaches and Echoes mod.)</color>"); return; }
            var d = SideAreas.Data;
            if (d == null) return;
            d["kind_" + Origin] = KindId;
            d.Remove("spent_" + Origin);
            NavNode here = p.GetMover().GetCurrentNavNode();
            NavNode spot = here?.GetLinks().Where(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.BlockCheck()).OrderBy(_ => Random.value).FirstOrDefault()
                           ?? SideAreas.RandomFreeNode(null);
            if (spot == null) { Plugin.Log.LogWarning("Ichor Deep: no free tile for the breach"); return; }
            Vector3 pos = spot.transform.position;
            d["breach_" + Origin] = new[] { pos.x, pos.y, pos.z };
            SideAreas.PlaceBreach(pos, Color);
            Game.LOG($"<color={Color}>Where Cerberus dissolved, the ichor keeps going down. A breach has opened into the Ichor Deep.</color> <color=#9FB7C9>Step on it to descend; it gets worse, and richer, with every depth.</color>");
            Plugin.Log.LogInfo($"Ichor Deep: unlocked, breach at {pos}");
        }

        /// <summary>The arena after the win: doors open, no Cerberus left standing (a reload must not replay its death).</summary>
        internal static void Settle()
        {
            if (!Won || Game.FLOOR != Origin || SideAreas.InSide) return;
            Room_Event_Cerberus room = Room_Event_Cerberus.GetInstance();
            if (room != null)
                foreach (Door door in room.roomDoors)
                    if (door != null) door.SetSeal(false);
            foreach (Character c in AI_Manager.instance.FindCharactersByTag("BOSS_CERBERUS").Where(c => c != null).ToList())
            {
                AI_Manager.instance.RemoveCharacter(c);
                c.GetMover()?.GetCurrentNavNode()?.ClearOccupant();
                Object.Destroy(c.gameObject);
                Plugin.Log.LogInfo("Ichor Deep: removed Cerberus's remains");
            }
        }

        // ---- the breach: where to start ---------------------------------------------------------------------------

        internal static void ShowEntry(NavNode pad)
        {
            int best = Best;
            string scale = $"+{PowerPerDepth.Value}% enemy power and +{HealthPerDepth.Value}% health per depth";
            var buttons = new List<(string, Action, Color?)> { ("Descend from depth 1", () => Go(pad, 1), UiKit.OnColor) };
            if (best > 1) buttons.Add(($"Resume at depth {best}", () => Go(pad, best), UiKit.OnColor));
            buttons.Add(("Not now", null, null));
            Choice.Show("SCCL_DeepEntry", $"<color={Color}>The Ichor Deep</color>",
                "What Cerberus was made of still runs down through the rock below the facility, and things live in it.\n\n"
                + $"Depth 1 is as dangerous as the final floor. Every depth below adds {scale}, more elites, and bigger, better caches and vaults. "
                + "Each layer has a rift one depth down. Any way out brings you back here."
                + (best > 0 ? $"\n\nDeepest reached: <color={Color}>depth {best}</color>." : ""),
                buttons);
        }

        private static void Go(NavNode pad, int depth)
        {
            startDepth = Math.Max(1, depth);
            Reroll(Origin);
            Plugin.Instance.StartCoroutine(SideAreas.Enter(pad));
        }

        /// <summary>For SideAreas.DoEnter: the depth this trip starts at.</summary>
        internal static int TakeStartDepth()
        {
            int d = startDepth;
            startDepth = 1;
            return d;
        }

        /// <summary>A fresh layer: a new place or floor to echo, a new roster and new anomalies (deeper = more hard ones).</summary>
        internal static void Reroll(int origin, int depth = 1)
        {
            var d = SideAreas.Data;
            if (d == null) return;
            KindSpec kind = SideAreaData.Kind(KindId);
            d.Remove("theme_" + origin);
            d.Remove("place_" + origin);
            d.Remove("anoms_" + origin);
            var rosters = SideAreaData.Rosters.Where(r => r.weight > 0 && (kind.rosters == null || kind.rosters.Count == 0 || kind.rosters.Contains(r.id, StringComparer.OrdinalIgnoreCase))).ToList();
            if (rosters.Count > 0)
            {
                int r = Random.Range(0, rosters.Sum(x => x.weight));
                d["roster_" + origin] = rosters.First(x => (r -= x.weight) < 0).id;
            }
            else d.Remove("roster_" + origin);
            // the kind's own roll, then one more hard anomaly every third depth (at most three more)
            var anoms = Echoes.Rolled(origin).ToList();
            int extra = Math.Min(3, (depth - 1) / 3);
            foreach (AnomalySpec a in SideAreaData.Anomalies.Where(a => a.hard && a.weight > 0 && a.timer == null && !anoms.Contains(a.id)
                                                                         && (kind.exclude == null || !kind.exclude.Contains(a.id, StringComparer.OrdinalIgnoreCase)))
                         .OrderBy(_ => Random.value).Take(extra))
                anoms.Add(a.id);
            d["anoms_" + origin] = anoms.ToArray();
        }

        /// <summary>For SideAreas.Build: a generated place most of the time, otherwise an echo of a real floor.</summary>
        internal static bool UsePlace() => Random.value < 0.6f;

        // ---- scaling --------------------------------------------------------------------------------------------

        private static float DifficultyPower()
        {
            switch (Game.GetDifficulty())
            {
                case 0: return -0.5f;
                case 2: return 0.5f;
                case 3: return 1f;
                default: return 0f;
            }
        }

        /// <summary>One enemy, once: final-floor stats raised for the depth, more health, a higher level, more XP.</summary>
        internal static void Scale(Character c, int depth)
        {
            if (c == null || depth < 1 || c.GetIsDead() || c.ai == null || c.ai.GetTeam() != AI.AI_TEAM.Hostile || c.HasTag(ScaledTag) || c.HasTag("BOSS")) return;
            c.SetTag(ScaledTag, depth);
            float power = PowerPerDepth.Value / 100f * depth;
            if (c is Mob m && !m.HasTag("NOPROFILE") && m.mobStatProfiles != null && m.mobStatProfiles.Length > 0)
            {
                m.powerLevelMod = DifficultyPower() + power;
                m.ApplyMobStatProfile(Math.Min(m.mobStatProfiles.Length - 1, Origin));
                m.CacheBaseStats();
            }
            int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * (1f + HealthPerDepth.Value / 100f * depth)));
            c.SetHealth(hp, hp);
            c.level += depth;
            if (c.xpValue > 0 && !c.HasTag("NOXP")) c.xpValue = Mathf.RoundToInt(c.xpValue * (1f + 0.25f * depth));
        }

        /// <summary>From SideAreas.Build, before the kind's rules: everyone the layer started with.</summary>
        internal static void OnLayerBuilt()
        {
            if (!Here) return;
            int depth = Depth;
            var enemies = Echoes.Enemies().ToList();
            foreach (Character c in enemies) Scale(c, depth);
            Player p = Player.instance;
            bool record = depth > Best;
            if (record) p.SetTag(BestTag, depth);
            Game.LOG($"<color={Color}>Depth {depth} of the Ichor Deep.</color> <color=#9FB7C9>Enemies here: +{PowerPerDepth.Value * depth}% power, +{HealthPerDepth.Value * depth}% health. "
                     + (record ? "Deeper than you've ever been." : $"Deepest so far: {Best}.") + "</color>");
            Character sample = enemies.FirstOrDefault(c => c is Mob);
            Plugin.Log.LogInfo($"Ichor Deep: depth {depth} ({Map.instance?.internalName}), {enemies.Count} enemies scaled"
                               + (sample != null ? $"; e.g. {sample.GetInternalName()} lvl {sample.level}, {sample.GetHealthMax()} HP, melee {sample.GetStat(STAT_TYPE.MeleePower)}, ranged {sample.GetStat(STAT_TYPE.RangedPower)}, armor {sample.GetStat(STAT_TYPE.Armor)}" : ""));
        }

        /// <summary>Anything that arrives later (rosters, anomalies, vault guards, waves) is scaled as it spawns.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.Spawn_Character), typeof(Character), typeof(Vector3), typeof(Quaternion), typeof(AI.AI_TEAM))]
        private static class Patch_Spawn
        {
            private static void Postfix(Character __result)
            {
                if (__result == null || Game.Game_isLoading || !Here) return;
                try { Scale(__result, Depth); }
                catch (Exception e) { Plugin.Log.LogError("Ichor Deep: scaling a spawn: " + e); }
            }
        }

        /// <summary>
        /// A map's rare spawn picks from its floor's rares without checking there are any. The final floor has none,
        /// so a Deep layer that rolled one crashed while being built. There the Excavation's rares stand in (the
        /// game's own rare logic, unchanged); anywhere else with none, the rare is skipped.
        /// </summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.SpawnRare), typeof(NavNode), typeof(string), typeof(Character), typeof(float))]
        private static class Patch_Rare
        {
            private static bool Prefix(string oRareSpawn, Character oCharacter, ref Character __result, out List<RareSpawn> __state)
            {
                __state = null;
                Level here = LevelManager.instance.GetLevel(Game.FLOOR);
                if (oRareSpawn != "" || here == null || (here.rareSpawns != null && here.rareSpawns.Count > 0)) return true;
                Level donor = Here ? Enumerable.Range(0, Origin).Reverse().Select(f => LevelManager.instance.GetLevel(f)).FirstOrDefault(l => l != null && l.rareSpawns != null && l.rareSpawns.Count > 0) : null;
                if (donor == null) { __result = null; return false; }
                __state = here.rareSpawns;
                here.rareSpawns = donor.rareSpawns;
                return true;
            }

            private static void Finalizer(List<RareSpawn> __state)
            {
                if (__state != null) LevelManager.instance.GetLevel(Game.FLOOR).rareSpawns = __state;
            }

            /// <summary>The rare re-applies its stat profile, which drops the depth's health; it goes back on top.</summary>
            private static void Postfix(Character __result)
            {
                if (__result == null || !Here || !__result.HasTag(ScaledTag)) return;
                int hp = Mathf.Max(1, Mathf.RoundToInt(__result.GetHealthMax() * (1f + HealthPerDepth.Value / 100f * __result.GetTag(ScaledTag))));
                __result.SetHealth(hp, hp);
            }
        }

        // ---- rewards ----------------------------------------------------------------------------------------------

        internal static float EliteBonus => Here ? Mathf.Min(0.45f, 0.03f * Depth) : 0f;

        internal static int CacheItems => 3 + Math.Min(7, Depth / 2);

        internal static RARITY CacheRarity => Depth >= 6 ? RARITY.Legendary : Depth >= 3 ? RARITY.Epic : RARITY.Rare;

        internal static int VaultBonus => Here ? Math.Min(3, Depth / 2) : 0;

        /// <summary>
        /// The game has no loot table for the final floor, and the Deep counts as that floor, so every cache, vault
        /// and drop came out empty. Here they draw from the deepest floor that has one (rarity rolls still use the
        /// real floor, a little better than the Excavation's).
        /// </summary>
        [HarmonyPatch(typeof(LootManager), "ScratchListCheck")]
        private static class Patch_LootTable
        {
            private static void Prefix(LootManager __instance, ref int lootFloorOverride)
            {
                if (!Here) return;
                LootTable[] tables = __instance.floorLootTables;
                if (tables == null || tables.Length == 0) return;
                int f = lootFloorOverride < 0 ? Game.FLOOR : lootFloorOverride;
                if (f < tables.Length && tables[f] != null) return;
                for (f = Math.Min(f, tables.Length - 1); f > 0 && tables[f] == null; f--) { }
                lootFloorOverride = f;
            }
        }

        // ---- floor 7 after the win ----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.LevelChangeComplete))]
        private static class Patch_LevelChange
        {
            private static void Postfix()
            {
                try { Settle(); }
                catch (Exception e) { Plugin.Log.LogError("Ichor Deep: settling the arena: " + e); }
            }
        }

        [HarmonyPatch(typeof(Room_Event_Cerberus), "Start")]
        private static class Patch_ArenaStart
        {
            private static void Postfix(Room_Event_Cerberus __instance)
            {
                if (!Won || __instance.truthPoolTrigger == null) return;
                __instance.truthPoolTrigger.completed = true;
                Object.Destroy(__instance.truthPoolTrigger.gameObject);
            }
        }
    }

    /// <summary>A small centred panel with a title, some text and a row of buttons (a null action just closes it).</summary>
    internal static class Choice
    {
        private static RectTransform panel;

        internal static bool Open => panel != null;

        internal static void Show(string name, string title, string body, IList<(string label, Action click, Color? color)> buttons, Transform parent = null)
        {
            Close();
            Canvas canvas = parent != null ? null : Menu_HUD.instance != null ? Menu_HUD.instance.GetComponentInParent<Canvas>() : null;
            Transform root = parent ?? canvas?.rootCanvas.transform;
            if (root == null) { Plugin.Log.LogWarning($"{name}: no canvas"); return; }
            TMP_FontAsset font = UiKit.FontFrom(Menu_HUD.instance != null ? (Component)Menu_HUD.instance : root);
            const float w = 820f, bh = 44f;
            var bodyText = UiKit.Label(root, body, font, 19, TextAlignmentOptions.TopLeft);
            bodyText.enableWordWrapping = true;
            float textH = bodyText.GetPreferredValues(body, w - 60f, 0f).y;
            Object.Destroy(bodyText.gameObject);
            float h = 70f + textH + 30f + bh + 26f;
            panel = UiKit.Panel(root, name, new Vector2(w, h), UiKit.PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.SetAsLastSibling();
            UiLook.Attach(panel, "Dialogs");
            UiLook.Grip(panel);

            RectTransform tb = UiKit.Rect(panel, "Title", new Vector2(w, 50f));
            UiKit.At(tb, 0f, 14f);
            UiKit.Label(tb, title, font, 32);
            RectTransform bb = UiKit.Rect(panel, "Body", new Vector2(w - 60f, textH + 10f));
            UiKit.At(bb, 30f, 70f);
            var t = UiKit.Label(bb, body, font, 19, TextAlignmentOptions.TopLeft);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;

            float gap = 14f, bw = Mathf.Min(300f, (w - 60f - gap * (buttons.Count - 1)) / buttons.Count);
            float x = (w - (bw * buttons.Count + gap * (buttons.Count - 1))) / 2f;
            foreach (var (label, click, color) in buttons)
            {
                Action act = click;
                Button b = UiKit.Button(panel, label, font, 19, new Vector2(bw, bh), () => { Close(); act?.Invoke(); }, color);
                b.name = "BTN_" + label;
                UiKit.At((RectTransform)b.transform, x, h - bh - 24f);
                x += bw + gap;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Game.SetLock(flag: true);
        }

        internal static void Close()
        {
            if (panel != null)
            {
                Object.Destroy(panel.gameObject);
                Game.SetLock(flag: false);
                Tooltip.instance?.Hide();
            }
            panel = null;
        }

        /// <summary>For the self-tests: presses the button whose label starts with this.</summary>
        internal static bool Press(string labelStart)
        {
            if (panel == null) return false;
            Button b = panel.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name.StartsWith("BTN_" + labelStart, StringComparison.OrdinalIgnoreCase));
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }
    }
}
