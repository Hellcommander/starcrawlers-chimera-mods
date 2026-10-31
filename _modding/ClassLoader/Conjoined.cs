using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The Conjoined class: several bodies, each a full character of its own (level, XP, trees and ranks, skill and perk
    /// points, gear, hotbar, effects). Inventory, credits, quests and story flags are shared. You play one body; the
    /// others walk beside you as companions. Swapping writes the current game with the other body's character in the
    /// player slot and runs the game's own loader on it, so every system sets itself up exactly as on a normal load.
    ///
    /// Saved in the mod's save slot (SideAreas.Data): "conj_bodies" (one dictionary per body) and "conj_active".
    /// Body companions carry the tags SCCL_BODY (= body id) and SCCL_BSTAMP (= spawn stamp); a companion whose stamp
    /// doesn't match its record (a stale copy left in another level's save) is removed.
    /// </summary>
    internal static class Conjoined
    {
        internal const string ClassId = "MOD_Conjoined";
        internal const string BodyTag = "SCCL_BODY";
        private const string StampTag = "SCCL_BSTAMP";
        /// <summary>Conjoined tree rank at which the 2nd, 3rd and 4th body join (rank 0 = tier 1).</summary>
        private static readonly int[] JoinRanks = { 0, 2, 4 };
        private static readonly string[] Numerals = { "", "", " II", " III", " IV", " V", " VI" };

        internal static bool Swapping;
        private static bool syncQueued;
        private static bool pickerDeferred;

        // ---- records ------------------------------------------------------------------------------------------

        internal class Body
        {
            internal readonly Dictionary<string, object> d;
            internal Body(Dictionary<string, object> d) { this.d = d; }
            private T G<T>(string k, T f = default) => d.TryGetValue(k, out object v) && v is T t ? t : f;
            internal int Id { get => G("id", 0); set => d["id"] = value; }
            internal string Name { get => G("name", "Body"); set => d["name"] = value; }
            internal PlayerSaveInfo Save { get => G<PlayerSaveInfo>("save"); set => d["save"] = value; }
            internal bool Downed { get => G("downed", false); set => d["downed"] = value; }
            internal int Hp { get => G("hp", -1); set => d["hp"] = value; }
            internal int HpMax { get => G("hpMax", 40); set => d["hpMax"] = value; }
            internal int Level { get => G("level", 1); set => d["level"] = value; }
            internal int ConjRank { get => G("conjRank", 0); set => d["conjRank"] = value; }
            internal int PendingXp { get => G("xp", 0); set => d["xp"] = value; }
            internal int Stamp { get => G("stamp", 0); set => d["stamp"] = value; }
            internal string Model { get => G("model", "Nemesis_Soldier"); set => d["model"] = value; }
            internal string Portrait { get => G<string>(Portraits.Key); set => d[Portraits.Key] = value; }
            internal string MapKey { get => G("map", ""); set => d["map"] = value; }
            internal float[] Pos { get => G<float[]>("pos"); set => d["pos"] = value; }
            internal bool Fresh { get => G("fresh", false); set => d["fresh"] = value; }
            internal Dictionary<string, object> Hotbar { get => G<Dictionary<string, object>>("hotbar"); set => d["hotbar"] = value; }
        }

        private static List<Dictionary<string, object>> Raw
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue("conj_bodies", out object v) && v is List<Dictionary<string, object>> l))
                    data["conj_bodies"] = l = new List<Dictionary<string, object>>();
                return l;
            }
        }

        internal static List<Body> Bodies => Raw?.Select(x => new Body(x)).ToList() ?? new List<Body>();

        internal static int ActiveId
        {
            get => SideAreas.Data != null && SideAreas.Data.TryGetValue("conj_active", out object v) && v is int i ? i : 0;
            set { if (SideAreas.Data != null) SideAreas.Data["conj_active"] = value; }
        }

        internal static Body Active => Bodies.FirstOrDefault(b => b.Id == ActiveId);

        /// <summary>The player has the Conjoined tree.</summary>
        internal static bool IsConjoined =>
            Player.instance != null && Player_SkillControl.instance != null && Player_SkillControl.instance.GetRank(ClassId) >= 0;

        internal static bool IsBody(Character c) => c != null && c != Player.instance && c.HasTag(BodyTag) && !c.GetIsDead();

        internal static List<Character> Npcs() =>
            AI_Manager.instance == null ? new List<Character>()
            : (Traverse.Create(AI_Manager.instance).Field("ais").GetValue<List<AI>>() ?? new List<AI>())
                .Where(a => a != null).Select(a => a.GetCharacter()).Where(IsBody).ToList();

        internal static Character NpcOf(Body b) => Npcs().FirstOrDefault(c => c.GetTag(BodyTag) == b.Id && c.GetTag(StampTag) == b.Stamp);

        /// <summary>Other bodies that are up (companions on this map or waiting to arrive).</summary>
        internal static int LivingOthers() => IsConjoined ? Bodies.Count(b => b.Id != ActiveId && !b.Downed) : 0;

        internal static bool Requirement(string what)
        {
            switch ((what ?? "").ToLowerInvariant())
            {
                case "livingbody": return !Swapping && Npcs().Count > 0;
                case "downedbody": return !Swapping && Bodies.Any(b => b.Id != ActiveId && b.Downed);
                default: return true;
            }
        }

        private static string MapKey => $"{Game.FLOOR}|{(Map.instance != null ? Map.instance.internalName : "")}";

        private static int AllowedBodies(int conjRank) => 1 + JoinRanks.Count(r => conjRank >= r);

        private static int PartyConjRank() =>
            Math.Max(Player_SkillControl.instance?.GetRank(ClassId) ?? -1, Bodies.Where(b => b.Id != ActiveId).Select(b => b.ConjRank).DefaultIfEmpty(-1).Max());

        // ---- companions ---------------------------------------------------------------------------------------

        /// <summary>Brings the companions in line with the records: adopt matching ones, drop stale ones, spawn missing.</summary>
        internal static void Sync()
        {
            if (Player.instance == null || AI_Manager.instance == null || Map.instance == null || Swapping) return;
            if (!IsConjoined)
            {
                foreach (Character c in Npcs()) Remove(c);
                return;
            }
            var raw = Raw;
            if (raw == null) return;
            if (raw.Count == 0 || Active == null)
            {
                // First time: the body you are playing becomes body 1.
                var me = new Body(new Dictionary<string, object>()) { Id = NextId(), Name = Player.instance.GetName() };
                raw.Add(me.d);
                ActiveId = me.Id;
            }
            string key = MapKey;
            foreach (Character c in Npcs())
            {
                Body b = Bodies.FirstOrDefault(x => x.Id == c.GetTag(BodyTag));
                if (b == null || b.Id == ActiveId || b.Downed || c.GetTag(StampTag) != b.Stamp || b.MapKey != key) Remove(c);
            }
            foreach (Body b in Bodies.Where(x => x.Id != ActiveId && !x.Downed))
            {
                Character npc = NpcOf(b);
                if (npc == null) npc = Spawn(b, b.MapKey == key ? b.Pos : null);
                if (npc != null) { b.MapKey = key; Dress(npc, b); }
            }
            UpdateStrain();
            CheckForNewBody();
        }

        /// <summary>
        /// A companion body fights as itself: its own level, trees at their ranks and equipped gear (the crew's
        /// Loadout), not the floor's stats. Its health is its own max health from when you last played it; a body
        /// you haven't played yet follows yours, like a hired crawler. Its gear shows on it too (Wear: weapon in
        /// hand, fitted pieces where its Nemesis body takes them, a rarity tint otherwise).
        /// </summary>
        private static void Dress(Character npc, Body b)
        {
            try { Loadout.Apply(npc, SpecOf(b), b.Hp, Crew.PlayerBaseHealth); }
            catch (Exception e) { Plugin.Log.LogWarning($"Conjoined: {b.Name}'s gear and trees: {e.Message}"); }
            npc.nameOverride = b.Name;
            try { Wear.Dress(npc, slot => Worn(b, slot), b.Model.Replace("Nemesis_", ""), b.Name); }
            catch (Exception e) { Plugin.Log.LogWarning($"Conjoined: dressing {b.Name}: {e.Message}"); }
        }

        /// <summary>A fresh copy of what the body has equipped in `slot` (the first match: the main hand for weapons), or null.</summary>
        private static Item Worn(Body b, EQUIP_TYPE slot)
        {
            if (!((b.Save?.equippedItems as EquipSaveInfo)?.equippedItems is object[] eq)) return null;
            foreach (object saved in eq)
            {
                Item it = Loadout.Rebuild(saved);
                if (it is Item_Equipment e && e.equipType == slot) return it;
                if (it != null) UnityEngine.Object.Destroy(it.gameObject);
            }
            return null;
        }

        internal static Loadout.Spec SpecOf(Body b)
        {
            PlayerSaveInfo save = b.Save;
            var spec = new Loadout.Spec { level = b.Level };
            if (save?.skillTrees != null)
                for (int i = 0; i < save.skillTrees.Length; i++)
                {
                    if (string.IsNullOrEmpty(save.skillTrees[i])) continue;
                    Skill_Tree t = Game.instance?.skills?.FirstOrDefault(s => s != null && s.internalName == save.skillTrees[i]);
                    if (t != null) spec.trees.Add((t, save.skillTreeRanks != null && i < save.skillTreeRanks.Length ? save.skillTreeRanks[i] : 0));
                }
            if ((save?.equippedItems as EquipSaveInfo)?.equippedItems is object[] eq) spec.gear = eq.Where(x => x != null).ToList();
            if (!b.Fresh && b.HpMax > 0)
            {
                // its health was recorded while it carried Spread Thin; it carries it again as a companion
                var thin = ModEffect.Specs.TryGetValue(ModEffect.MakeId(ClassId, "SpreadThin"), out var rt) ? rt.spec.stats : null;
                int per = thin != null && thin.TryGetValue("HealthMax", out int v) ? v : 0;
                spec.healthMax = Math.Max(10, b.HpMax - per * Actions.StacksOn(Player.instance, ModEffect.MakeId(ClassId, "SpreadThin")));
            }
            return spec;
        }

        private static Character Spawn(Body b, float[] at)
        {
            Character prefab = AI_Manager.instance.GetCharacter(b.Model) ?? AI_Manager.instance.GetCharacter("Nemesis_Soldier");
            if (prefab == null) { Plugin.Log.LogWarning($"Conjoined: no model '{b.Model}' for {b.Name}"); return null; }
            NavNode node = null;
            if (at != null && at.Length == 3) node = Map.instance.GetNearestNavNodeAt(new Vector3(at[0], at[1], at[2]), 0.75f);
            if (node == null || node.IsOccupied()) node = Map.instance.GetClosestNode(Player.instance.GetMover().GetCurrentNavNode(), 4f, validNodesOnly: true);
            if (node == null || node.IsOccupied()) return null;
            b.Stamp = b.Stamp + 1;
            Character c = AI_Manager.instance.Spawn_Character(prefab, node.transform.position, AI.AI_TEAM.Player);
            if (c == null) return null;
            foreach (string t in c.tags.Where(t => t != null && t.tagName != null).Select(t => t.tagName).ToList())
                if (t.StartsWith("NEMESIS") || t == "BOSS" || t == "ELITE" || t.StartsWith("KILLSQUAD") || t.StartsWith("RARESPAWN")) c.RemoveTag(t);
            c.SetTag(BodyTag, b.Id);
            c.SetTag(StampTag, b.Stamp);
            c.AddTag("NOXP");
            c.xpValue = 0;
            if (c.ai != null) { c.ai.SetTarget(null); c.ai.ChangeState(AI.AI_STATE.Idle); }
            Dress(c, b);
            return c;
        }

        private static void Remove(Character c)
        {
            if (c == null) return;
            AI_Manager.instance.RemoveCharacter(c);
            c.GetMover()?.GetCurrentNavNode()?.ClearOccupant();
            UnityEngine.Object.Destroy(c.gameObject);
        }

        private static int NextId() => Bodies.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;

        /// <summary>Companion health and positions into the records (before any save or swap).</summary>
        internal static void Record()
        {
            if (!IsConjoined) return;
            string key = MapKey;
            foreach (Body b in Bodies.Where(x => x.Id != ActiveId && !x.Downed))
            {
                Character npc = NpcOf(b);
                if (npc == null) continue;
                b.Hp = npc.GetHealth();
                b.HpMax = npc.GetHealthMax();
                Vector3 p = npc.transform.position;
                b.Pos = new[] { p.x, p.y, p.z };
                b.MapKey = key;
            }
            Body me = Active;
            if (me != null)
            {
                me.Hp = Player.instance.GetHealth();
                me.HpMax = Player.instance.GetHealthMax();
                me.Level = Player.instance.level;
                me.ConjRank = Player_SkillControl.instance.GetRank(ClassId);
            }
        }

        // ---- strain -------------------------------------------------------------------------------------------

        /// <summary>Every other living body costs each body some damage and health; at Conjoined rank 4 one is forgiven.</summary>
        internal static void UpdateStrain()
        {
            if (!IsConjoined || EffectManager.instance == null) return;
            string id = ModEffect.MakeId(ClassId, "SpreadThin");
            int n = LivingOthers() - (PartyConjRank() >= 4 ? 1 : 0);
            foreach (Character c in new[] { (Character)Player.instance }.Concat(Npcs()))
            {
                Effect e = c.effects.GetEffect(id);
                if (n <= 0) { if (e != null) { e.RemoveInstant(); c.CalculateStats(); } continue; }
                if (e == null) e = EffectManager.instance.ApplyEffect(id, c);
                (e as ModEffect)?.SetStacks(n);
            }
        }

        // ---- swapping -----------------------------------------------------------------------------------------

        internal static bool InCombat => AI_Manager.instance != null && AI_Manager.instance.outOfCombatTimer > 0;

        private static ModAbility TradeAbility() =>
            Player_AbilityControl.instance?.GetAbilities().OfType<ModAbility>()
                .FirstOrDefault(a => a.Spec?.actions?.Any(x => (x.@do ?? "").ToLowerInvariant() == "swapbody") == true);

        /// <summary>
        /// Switching from the party panel: free out of combat; in combat it needs Trade Bodies ready and uses it up.
        /// Null when allowed, else the reason.
        /// </summary>
        internal static string SwitchBlocker(Body b)
        {
            if (Swapping || Game.Game_isLoading) return "Already switching.";
            if (b == null || b.Id == ActiveId) return "You are this body.";
            if (b.Downed) return "This body is down. Knit Together brings it back.";
            if (NpcOf(b) == null) return "This body isn't here yet.";
            if (Game.RESOLVING || Game.TARGETING) return "Not in the middle of a turn.";
            if (!InCombat) return null;
            ModAbility t = TradeAbility();
            if (t == null) return "In combat you need Trade Bodies to switch.";
            if (!t.CanUse()) return "In combat: Trade Bodies isn't ready.";
            return null;
        }

        internal static bool TrySwitch(Body b)
        {
            string why = SwitchBlocker(b);
            if (why != null) { Game.LOG(why); AudioManager.PlayEvent("event:/UI/UI_Error"); return false; }
            Plugin.Instance.StartCoroutine(SwapTo(b.Id, handoff: false));
            return true;
        }

        internal static void SwapToNearest(string source)
        {
            Character npc = Npcs().OrderBy(c => Vector3.Distance(c.transform.position, Player.instance.transform.position)).FirstOrDefault();
            if (npc == null) { Game.LOG($"{source}: no other body is standing with you."); return; }
            Plugin.Instance.StartCoroutine(SwapTo(npc.GetTag(BodyTag), handoff: false, trade: true));
        }

        /// <summary>
        /// Puts body <paramref name="id"/> in control. With handoff the body you leave has fallen. With trade (the Trade
        /// Bodies skill) the two bodies also swap places: you stay where you stood, the body you leave takes the other's spot.
        /// </summary>
        internal static IEnumerator SwapTo(int id, bool handoff, bool trade = false)
        {
            if (Swapping) yield break;
            Swapping = true;
            Player.instance.AddTag("IMMORTAL");
            string tmp = Path.Combine(Application.persistentDataPath, "SCCL_swap.tmp");
            bool loaded = false;
            // Let the ability that asked for this finish first: the reload destroys it along with the player.
            yield return null;
            yield return null;
            float waited = 0f;
            while ((Game.RESOLVING || Game.TARGETING || Game.Game_isLoading) && waited < 4f) { waited += Time.unscaledDeltaTime; yield return null; }
            try
            {
                Record();
                Body from = Active, to = Bodies.FirstOrDefault(b => b.Id == id);
                Character npc = to != null ? NpcOf(to) : null;
                if (from == null || to == null || to.Downed || to.Save == null || npc == null) { Plugin.Log.LogWarning($"Conjoined: can't swap to body {id}"); yield break; }

                Player.instance.RemoveTag("IMMORTAL");
                var snap = (PlayerSaveInfo)Player.instance.Serialize();
                Vector3 here = Player.instance.transform.position;
                from.Save = snap;
                from.Downed = handoff;
                if (handoff) from.Hp = 0;
                from.Pos = new[] { here.x, here.y, here.z };
                from.MapKey = MapKey;
                from.Model = ModelFor(snap.skillTrees);
                from.Hotbar = TakeHotbarPages();

                PlayerSaveInfo next = to.Save;
                Share(snap, next);
                Vector3 there = npc.transform.position;
                if (trade) from.Pos = new[] { there.x, there.y, there.z };
                next.playerPos = trade ? here : there;
                next.playerRot = snap.playerRot;
                next.camRot = snap.camRot;
                next.savedHealth = to.Hp > 0 ? to.Hp : 1;
                PutHotbarPages(to.Hotbar);
                to.Save = null;
                Remove(npc);
                ActiveId = to.Id;

                var sgi = (SaveGameInfo)Game.instance.Serialize();
                sgi.playerData = next;
                var sd = new SaveData
                {
                    saveName = snap.playerName, timeStamp = DateTime.Now.ToString("HH:mm\\  dd-MM-yyyy"), iSaveData = sgi,
                    difficulty = Game.GetDifficulty(), ironmode = Game.GetIronmode(),
                };
                using (FileStream fs = File.Create(tmp)) new BinaryFormatter().Serialize(fs, sd);
                Game.LOG(handoff ? $"<color=#FF7272>{from.Name} falls.</color> {to.Name} takes over."
                    : trade ? $"You and {to.Name} trade places, and you step into it." : $"You step into {to.Name}.");
                loaded = true;
            }
            finally
            {
                if (!loaded)
                {
                    Swapping = false;
                    if (Player.instance != null) Player.instance.RemoveTag("IMMORTAL");
                }
            }
            yield return Game.instance.StartCoroutine(Game.instance.LoadGame(tmp));
            try { File.Delete(tmp); } catch { }
            Swapping = false;
            try { AfterSwapIn(); }
            catch (Exception e) { Plugin.Log.LogError("Conjoined after swap: " + e); }
            Sync();
        }

        /// <summary>What every body shares, taken from the body you're leaving.</summary>
        private static void Share(PlayerSaveInfo from, PlayerSaveInfo to)
        {
            to.playerName = from.playerName;
            to.inventoryItems = from.inventoryItems;
            to.chimeraLinq_local = from.chimeraLinq_local;
            to.questRewards = from.questRewards;
            to.turnCount = from.turnCount;
            to.tags = from.tags;
            to.linq_name = from.linq_name;
            to.linq_header = from.linq_header;
            to.linq_bio = from.linq_bio;
            to.linq_pic = from.linq_pic;
            to.mRank = from.mRank;
            to.gRank = from.gRank;
            to.iRank = from.iRank;
        }

        private static Dictionary<string, object> TakeHotbarPages()
        {
            var d = new Dictionary<string, object>();
            if (SideAreas.Data == null) return d;
            foreach (string k in SideAreas.Data.Keys.Where(k => k.StartsWith("hotbar_")).ToList()) { d[k] = SideAreas.Data[k]; SideAreas.Data.Remove(k); }
            return d;
        }

        private static void PutHotbarPages(Dictionary<string, object> d)
        {
            if (SideAreas.Data == null) return;
            foreach (string k in SideAreas.Data.Keys.Where(k => k.StartsWith("hotbar_")).ToList()) SideAreas.Data.Remove(k);
            if (d != null) foreach (var kv in d) SideAreas.Data[kv.Key] = kv.Value;
        }

        /// <summary>Catch-up XP, and for a body played for the first time: full health, its abilities on the hotbar.</summary>
        private static void AfterSwapIn()
        {
            Body me = Active;
            Player p = Player.instance;
            if (me == null || p == null) return;
            int xp = me.PendingXp;
            me.PendingXp = 0;
            if (xp > 0) GiveXp(p, xp);
            if (me.Fresh)
            {
                me.Fresh = false;
                foreach (Skill_Tree t in Player_SkillControl.instance.skillTrees.Where(t => t != null))
                    foreach (Skill_Tier tier in t.skillTiers.Where(x => x != null && x.isUnlocked))
                    {
                        if (tier.ability != null && !Menu_HUD.instance.HasAbility(tier.ability)) Menu_HUD.instance.AddAbility(tier.ability);
                        if (!string.IsNullOrEmpty(tier.effect) && EffectManager.instance.CanUseOn(tier.effect, p)) EffectManager.instance.ApplyEffect(tier.effect, p);
                    }
                p.CalculateStats();
                p.SetHealth(p.GetHealthMax());
                Menu_HUD.instance.Refresh();
            }
            // A swap in combat shouldn't be followed by an instant swap back from the other body.
            ModAbility trade = TradeAbility();
            if (trade != null && InCombat) trade.SetCooldownAmount(trade.cooldown);
        }

        private static bool givingPending;

        /// <summary>XP earned while this body was a companion, paid through the game's own AddXp.</summary>
        private static void GiveXp(Player p, int amount)
        {
            Game.LOG($"{Active?.Name}: XP gained alongside the others.");
            givingPending = true;
            try
            {
                // Two levels up: AddXp pays it unscaled. It levels up once per call, so a big backlog takes a few
                // calls (each extra call adds AddXp's minimum of 1 XP).
                p.AddXp(amount, p.level + 2, "");
                for (int i = 0; i < 20 && p.xpToNextLevel > 0 && p.xp >= p.xpToNextLevel; i++) p.AddXp(0, p.level + 2, "");
            }
            finally { givingPending = false; }
        }

        private static string ModelFor(string[] trees)
        {
            string main = trees?.FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != ClassId);
            Skill_Tree tree = main == null ? null : Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == main);
            switch (tree?.skillClass)
            {
                case SKILL_CLASS.Guile: return "Nemesis_Smuggler";
                case SKILL_CLASS.Intellect: return "Nemesis_Engineer";
                default: return "Nemesis_Soldier";
            }
        }

        // ---- new bodies, revive -------------------------------------------------------------------------------

        internal static void CheckForNewBody()
        {
            if (!IsConjoined || Swapping || pickerDeferred || BodyPicker.Open) return;
            if (Bodies.Count >= AllowedBodies(PartyConjRank())) return;
            BodyPicker.Show(TreeChoices());
        }

        internal static void DeferPicker() => pickerDeferred = true;

        /// <summary>How many classes a new body picks besides Conjoined: as many as the body you're playing has.</summary>
        internal static int TreeChoices() =>
            Math.Max(0, Player_SkillControl.instance.skillTrees.Count(t => t != null && t.internalName != ClassId));

        /// <summary>A new body at your level: Conjoined plus <paramref name="trees"/>, every point unspent, nothing equipped.</summary>
        internal static Body CreateBody(IList<string> trees)
        {
            var raw = Raw;
            if (raw == null || Player.instance == null) return null;
            Record();
            var src = (PlayerSaveInfo)Player.instance.Serialize();
            var ac = src.abilities as AbilityControlSaveInfo;
            int spent = src.skillTreeRanks.Where((r, i) => !string.IsNullOrEmpty(src.skillTrees[i])).Sum();
            var p = new PlayerSaveInfo
            {
                playerName = src.playerName, playerLevel = src.playerLevel, xp = 0, xpToNextLevel = src.xpToNextLevel,
                savedHealth = 1, savedStamina = 9999, mRank = src.mRank, gRank = src.gRank, iRank = src.iRank,
                skillTrees = new string[7], skillTreeRanks = new int[7], weaponSet = 1,
                hotbar_slotNames = Enumerable.Repeat("EMPTY", src.hotbar_slotNames?.Length ?? 21).ToArray(),
                equippedItems = new EquipSaveInfo { equippedItems = new object[((src.equippedItems as EquipSaveInfo)?.equippedItems?.Length) ?? 8] },
                abilities = new AbilityControlSaveInfo
                {
                    abilitySaveInfo = new object[0], skillPoints = spent + (ac?.skillPoints ?? 0),
                    perkPoints_total = ac?.perkPoints_total ?? 0, perkPoints_available = ac?.perkPoints_total ?? 0,
                },
                effects = new object[0], tags = new List<Tag>(), playerPos = src.playerPos, playerRot = src.playerRot, camRot = src.camRot,
            };
            p.skillTrees[0] = ClassId;
            for (int i = 0; i < trees.Count && i < 6; i++) p.skillTrees[i + 1] = trees[i];
            for (int i = 0; i < 7; i++) if (p.skillTrees[i] == null) p.skillTrees[i] = "";
            int n = raw.Count + 1;
            var b = new Body(new Dictionary<string, object>())
            {
                Id = NextId(), Name = (Active?.Name ?? Player.instance.GetName()) + (n < Numerals.Length ? Numerals[n] : $" {n}"),
                Save = p, Level = src.playerLevel, HpMax = Player.instance.GetHealthMax(), Hp = Player.instance.GetHealthMax(),
                ConjRank = 0, Model = ModelFor(p.skillTrees), Fresh = true,
            };
            raw.Add(b.d);
            Game.LOG($"<color=#FFD15C>{b.Name} pulls free and joins you.</color>");
            Sync();
            return b;
        }

        internal static void ReviveOne(string source, int pct)
        {
            Body b = Bodies.FirstOrDefault(x => x.Id != ActiveId && x.Downed);
            if (b == null) { Game.LOG($"{source}: none of your bodies has fallen."); return; }
            b.Downed = false;
            b.Hp = Mathf.Max(1, b.HpMax * Mathf.Clamp(pct, 1, 100) / 100);
            b.Pos = null;
            Game.LOG($"<color=#9FD99F>{source}: {b.Name} is knitted back together.</color>");
            Sync();
        }

        // ---- patches ------------------------------------------------------------------------------------------

        private static IEnumerator SyncSoon()
        {
            if (syncQueued) yield break;
            syncQueued = true;
            yield return null;
            while (Game.Game_isLoading || Player.instance == null) yield return null;
            yield return null;
            syncQueued = false;
            pickerDeferred = false;
            try { Sync(); }
            catch (Exception e) { Plugin.Log.LogError("Conjoined sync: " + e); }
        }

        [HarmonyPatch(typeof(Level), nameof(Level.OnPlayerEntered))]
        private static class Patch_Entered
        {
            private static void Postfix() { if (!Swapping) Plugin.Instance.StartCoroutine(SyncSoon()); }
        }

        /// <summary>Before any level is written (saving, stairs), companion health and positions go into the records.</summary>
        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SerializeLevel))]
        private static class Patch_Serialize
        {
            private static void Prefix() { try { if (!Swapping) Record(); } catch (Exception e) { Plugin.Log.LogError("Conjoined record: " + e); } }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                if (Swapping || !IsConjoined) return;
                try { Record(); UpdateStrain(); CheckForNewBody(); }
                catch (Exception e) { Plugin.Log.LogError("Conjoined turn: " + e); }
            }
        }

        [HarmonyPatch(typeof(Skill_Tree), nameof(Skill_Tree.SetRank))]
        private static class Patch_Rank
        {
            private static void Postfix() { if (!Game.Game_isLoading && !Swapping && Player.instance != null) pickerDeferred = false; }
        }

        /// <summary>A companion body that dies is down, not gone: Knit Together brings it back.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_BodyDies
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == Player.instance || __instance.GetIsDead() || !__instance.HasTag(BodyTag)) return;
                Body b = Bodies.FirstOrDefault(x => x.Id == __instance.GetTag(BodyTag) && x.Stamp == __instance.GetTag(StampTag));
                if (b == null) return;
                b.Downed = true;
                b.Hp = 0;
                Game.LOG($"<color=#FF7272>{b.Name} goes down.</color>");
                __instance.AddTag("NOCORPSE");
                Plugin.Instance.StartCoroutine(StrainSoon());
            }
        }

        private static IEnumerator StrainSoon() { yield return null; UpdateStrain(); }

        /// <summary>When the body you play would die and another stands with you, control passes to it.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Die))]
        private static class Patch_PlayerDies
        {
            private static bool Prefix(Player __instance)
            {
                if (Swapping) { __instance.SetHealth(1); return false; }
                if (__instance.GetIsDead() || !IsConjoined) return true;
                Character npc = Npcs().OrderBy(c => Vector3.Distance(c.transform.position, __instance.transform.position)).FirstOrDefault();
                if (npc == null) return true;
                __instance.SetHealth(1);
                Plugin.Instance.StartCoroutine(SwapTo(npc.GetTag(BodyTag), handoff: true));
                return false;
            }
        }

        /// <summary>Every body that's up earns the XP you do.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.AddXp))]
        private static class Patch_Xp
        {
            private static void Prefix(Player __instance, out (int xp, int lvl) __state) => __state = (__instance.xp, __instance.level);

            private static void Postfix(Player __instance, (int xp, int lvl) __state)
            {
                if (!IsConjoined || givingPending) return;
                int gained = __instance.level > __state.lvl ? __instance.xp + (__instance.xpToNextLevel - __state.xp) : __instance.xp - __state.xp;
                if (__instance.level > __state.lvl) gained = Math.Max(1, gained);
                if (gained <= 0) return;
                foreach (Body b in Bodies.Where(x => x.Id != ActiveId && !x.Downed)) b.PendingXp = b.PendingXp + gained;
            }
        }

        /// <summary>Bodies aren't pets: they don't take pet slots and the lashers don't eat them.</summary>
        [HarmonyPatch(typeof(PetHandling), nameof(PetHandling.IsEdible))]
        private static class Patch_NotEdible
        {
            private static void Postfix(Character c, ref bool __result) { if (__result && c != null && c.HasTag(BodyTag)) __result = false; }
        }
    }
}
