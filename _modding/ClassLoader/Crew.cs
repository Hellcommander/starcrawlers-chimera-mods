using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Hired crew: StarCrawlers-style party members built on the game's Nemesis crawlers (the humanoid rivals that
    /// share the player classes). A crew member keeps its body, AI and class moves, but its power no longer comes
    /// from the floor it stands on: it starts from the weakest stat profile of its body and adds
    ///   - a baseline for your level (it levels with you),
    ///   - the tier bonuses of its class tree, at a rank that grows with your level,
    ///   - the stats and resistances of the gear you give it,
    /// and its basic attack swings (or fires) the weapon you give it.
    ///
    /// Records live in the mod's save slot (SideAreas.Data["crew"]): one dictionary per member with its gear as
    /// the game's own item save data. Crew in the world carry SCCL_CREW (= id) and SCCL_CSTAMP (= spawn stamp);
    /// a crawler whose stamp doesn't match its record (a stale copy in another level's save) is removed.
    /// A crew member that falls is back on the next floor at half health.
    ///
    /// Team play is an option ([Crew] TeamPlay, off by default). Crawlers are hired in side areas, through the
    /// faction that supplies them (Valued standing, credits and favour). Leading a crew costs you some of your own
    /// punch (Leading a Crew: less damage per crawler), and they want wages every floor: miss two paydays and they
    /// walk off with their gear, or (with [Crew] Mutiny) may turn on you.
    /// </summary>
    internal static class Crew
    {
        internal static ConfigEntry<bool> TeamPlay, Mutiny;
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<int> MaxCrew, Penalty;

        /// <summary>Tests switch the crew on without touching the player's config file.</summary>
        internal static bool ForceOn;
        /// <summary>Tests: hire outside side areas, and force how an unpaid crawler reacts (null = roll).</summary>
        internal static bool TestAnywhere;
        internal static bool? TestMutiny;
        internal static bool On => ForceOn || (TeamPlay != null && TeamPlay.Value);

        internal const string CrewTag = "SCCL_CREW", StampTag = "SCCL_CSTAMP";

        internal class Kind
        {
            public string id, name, prefab, tree, blurb, faction;
            public bool melee;
        }

        internal static readonly Kind[] Kinds =
        {
            new Kind { id = "Soldier", name = "Soldier", prefab = "Nemesis_Soldier", tree = "SKILLTREE_Soldier", faction = "Brassjaw", melee = false, blurb = "Rifle and grenades. Fires ranged weapons." },
            new Kind { id = "Cyberninja", name = "Cyberninja", prefab = "Nemesis_Cyberninja", tree = "SKILLTREE_Cyberninja", faction = "Nullrow", melee = true, blurb = "Dashes in and leaves shadow clones. Swings melee weapons." },
            new Kind { id = "ForcePsyker", name = "Force Psyker", prefab = "Nemesis_ForcePsyker", tree = "SKILLTREE_ForcePsyker", faction = "OpenWard", melee = true, blurb = "Pulls foes close and smashes them. Swings melee weapons." },
            new Kind { id = "Smuggler", name = "Smuggler", prefab = "Nemesis_Smuggler", tree = "SKILLTREE_Smuggler", faction = "Halyard", melee = false, blurb = "Shoots on the move and slips away. Fires ranged weapons." },
        };

        /// <summary>The gear slots a crew member has, in screen order.</summary>
        internal static readonly EQUIP_TYPE[] Slots = { EQUIP_TYPE.Weapon, EQUIP_TYPE.Head, EQUIP_TYPE.Torso, EQUIP_TYPE.Hands, EQUIP_TYPE.Legs, EQUIP_TYPE.Feet };

        private static readonly string[] Names =
        {
            "Vex Harrow", "Juno Pike", "Brask Teller", "Ilka Moraine", "Dace Ferro", "Sable Quin", "Otto Marrin", "Rhee Calder",
            "Nim Castell", "Garo Venn", "Teska Lund", "Mabry Coil", "Pell Sorrow", "Kit Ambrose", "Hesk Doyle", "Wren Okafor",
        };

        internal static void Bind(ConfigFile cfg)
        {
            TeamPlay = cfg.Bind("Crew", "TeamPlay", false, "Team play, StarCrawlers style: hire crawlers in side areas (through the factions) who level with you, wear the gear you give them and want wages every floor.");
            Key = cfg.Bind("Crew", "Key", KeyCode.F6, "Opens the crew screen.");
            MaxCrew = cfg.Bind("Crew", "MaxCrew", 2, "Most crew members at once (1-3).");
            Penalty = cfg.Bind("Crew", "DamagePenalty", 12, "Leading a Crew: percent less damage you and each crawler deal per other crew member (0 = no penalty). Like Conjoined's Spread Thin, it offsets having several skilled fighters.");
            Mutiny = cfg.Bind("Crew", "Mutiny", true, "Crawlers owed two paydays may turn on you instead of just walking off.");
        }

        // ---- records ------------------------------------------------------------------------------------------

        internal class Member
        {
            internal readonly Dictionary<string, object> d;
            internal Member(Dictionary<string, object> d) { this.d = d; }
            private T G<T>(string k, T def) => d.TryGetValue(k, out object v) && v is T t ? t : def;
            internal int Id { get => G("id", 0); set => d["id"] = value; }
            internal string Name { get => G("name", "Crawler"); set => d["name"] = value; }
            internal string KindId { get => G("kind", "Soldier"); set => d["kind"] = value; }
            internal int Stamp { get => G("stamp", 0); set => d["stamp"] = value; }
            internal int Hp { get => G("hp", -1); set => d["hp"] = value; }
            internal bool Downed { get => G("downed", false); set => d["downed"] = value; }
            internal string MapKey { get => G("map", ""); set => d["map"] = value; }
            internal float[] Pos { get => G<float[]>("pos", null); set => d["pos"] = value; }
            internal int Missed { get => G("missed", 0); set => d["missed"] = value; }
            internal bool Turning { get => G("turning", false); set => d["turning"] = value; }
            internal Dictionary<string, object> Gear
            {
                get
                {
                    if (!(d.TryGetValue("gear", out object v) is true && v is Dictionary<string, object> g)) { g = new Dictionary<string, object>(); d["gear"] = g; }
                    return g;
                }
            }
            internal Kind Kind => Kinds.FirstOrDefault(k => k.id == KindId) ?? Kinds[0];
        }

        private static List<object> Raw
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue("crew", out object v) && v is List<object> list)) { list = new List<object>(); data["crew"] = list; }
                return list;
            }
        }

        internal static List<Member> Members => (Raw ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => new Member(x)).ToList();

        internal static int HireCost => 60 + 30 * Math.Max(1, Player.instance != null ? Player.instance.level : 1);
        internal const int HireFavour = 3;

        /// <summary>Wages per crawler, every floor.</summary>
        internal static int Wage => 15 + 10 * Math.Max(1, Player.instance != null ? Player.instance.level : 1);

        private static string MapKey => $"{Game.FLOOR}|{(Map.instance != null ? Map.instance.internalName : "")}";

        // ---- hiring and letting go ----------------------------------------------------------------------------

        /// <summary>Why you can't hire at all right now, or null.</summary>
        internal static string CantHire()
        {
            if (!On) return "team play is off ([Crew] TeamPlay in the config)";
            if (Raw == null || Player.instance == null) return "not in a run";
            if (Members.Count >= Mathf.Clamp(MaxCrew.Value, 1, 3)) return $"your crew is full ({Mathf.Clamp(MaxCrew.Value, 1, 3)})";
            if (!SideAreas.InSide && !TestAnywhere) return "crawlers are hired in side areas (through a breach)";
            if (Menu_Inventory.instance == null || Menu_Inventory.instance.GetCredits() < HireCost) return $"hiring costs {HireCost} credits";
            return null;
        }

        internal static FactionSpec Faction(Kind k) => Factions.Get(k.faction);

        /// <summary>Why this kind of crawler can't be hired right now, or null.</summary>
        internal static string CantHire(Kind k)
        {
            string why = CantHire();
            if (why != null) return why;
            FactionSpec f = Faction(k);
            if (f == null) return null; // factions off or missing: credits only
            if (!Factions.Known(f)) return $"you don't know anyone at {f.name} yet";
            if (Factions.Rep(f) <= 10) return $"{f.name} won't lend you anyone below Valued";
            if (Factions.Favour(f) < HireFavour) return $"needs {HireFavour} favour with {f.name}";
            return null;
        }

        internal static Member Hire(string kindId, bool free = false)
        {
            Kind k = Kinds.FirstOrDefault(x => x.id == kindId);
            if (k == null || (!free && CantHire(k) != null)) return null;
            if (!free)
            {
                Menu_Inventory.instance.AdjustCredits(-HireCost);
                FactionSpec f = Faction(k);
                if (f != null) Factions.AdjustFavour(f, -HireFavour);
            }
            var used = new HashSet<string>(Members.Select(m => m.Name));
            var m = new Member(new Dictionary<string, object>())
            {
                Id = Members.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1,
                Name = Names.Where(n => !used.Contains(n)).OrderBy(_ => Random.value).FirstOrDefault() ?? "Crawler",
                KindId = k.id,
            };
            Raw.Add(m.d);
            Game.LOG($"<color=#7FE0C8>{m.Name}</color> the {k.name} joins your crew.");
            AudioManager.PlayEvent("event:/UI/UI_CashRegister");
            Sync();
            return m;
        }

        internal static void Dismiss(Member m)
        {
            foreach (EQUIP_TYPE slot in Slots.ToList()) Unequip(m, slot);
            Character c = NpcOf(m);
            if (c != null) Remove(c);
            Raw.Remove(m.d);
            Game.LOG($"{m.Name} leaves your crew.");
            UpdatePenalty();
        }

        // ---- gear ---------------------------------------------------------------------------------------------

        /// <summary>The item in a slot, rebuilt from its save data (not in the world: read it, then Destroy it).</summary>
        internal static Item Peek(Member m, EQUIP_TYPE slot)
        {
            if (!m.Gear.TryGetValue(slot.ToString(), out object saved) || !(saved is ItemSaveInfo info)) return null;
            Item it = ItemDatabase.instance.GetItem(info.itemId);
            if (it == null) return null;
            it.Deserialize(saved);
            return it;
        }

        /// <summary>Why this item can't go on this crew member, or null.</summary>
        internal static string CantEquip(Member m, Item item)
        {
            if (!(item is Item_Equipment eq)) return "that isn't gear";
            if (!Slots.Contains(eq.equipType)) return $"crew don't use {eq.equipType.ToString().ToLowerInvariant()} items";
            if (item is Item_Weapon w)
            {
                bool melee = w.weaponType == WEAPON_TYPE.Melee || w.weaponType == WEAPON_TYPE.HeavyMelee;
                bool ranged = w.weaponType == WEAPON_TYPE.Ranged || w.weaponType == WEAPON_TYPE.HeavyRanged;
                if (!melee && !ranged) return "crew only use melee and ranged weapons";
                if (melee != m.Kind.melee) return $"a {m.Kind.name} uses {(m.Kind.melee ? "melee" : "ranged")} weapons";
            }
            return null;
        }

        /// <summary>Moves an item from your inventory onto the crew member (anything already in that slot comes back to you).</summary>
        internal static bool Equip(Member m, Item item)
        {
            if (CantEquip(m, item) != null) return false;
            EQUIP_TYPE slot = ((Item_Equipment)item).equipType;
            if (m.Gear.ContainsKey(slot.ToString()) && !Unequip(m, slot)) return false;
            object saved = item.Serialize();
            Menu_Inventory.instance.RemoveItem(item, null, isSilent: true);
            UnityEngine.Object.Destroy(item.gameObject);
            m.Gear[slot.ToString()] = saved;
            AudioManager.PlayEvent("event:/Inventory/Equip_Weapon_Ranged");
            Refresh(m);
            return true;
        }

        /// <summary>Gives the item in a slot back to your inventory (if there's room).</summary>
        internal static bool Unequip(Member m, EQUIP_TYPE slot)
        {
            Item it = Peek(m, slot);
            if (it == null) { m.Gear.Remove(slot.ToString()); Refresh(m); return true; }
            if (!Menu_Inventory.TakeCheck(it)) { UnityEngine.Object.Destroy(it.gameObject); Game.LOG("No room in your pack for that."); return false; }
            Menu_Inventory.Take(it);
            m.Gear.Remove(slot.ToString());
            Refresh(m);
            return true;
        }

        // ---- stats --------------------------------------------------------------------------------------------

        /// <summary>The class tree's rank for this crew member: one tier at the start, another every two levels.</summary>
        internal static int TreeRank(Member m)
        {
            Skill_Tree t = Tree(m);
            int lvl = Player.instance != null ? Player.instance.level : 1;
            return t == null ? -1 : Mathf.Clamp((lvl - 1) / 2, 0, t.skillTiers.Count - 1);
        }

        internal static Skill_Tree Tree(Member m) => Game.instance?.skills?.FirstOrDefault(s => s != null && s.internalName == m.Kind.tree);

        /// <summary>The crew member's sheet: your level, its class tree at its rank, and the gear you lent it.</summary>
        internal static Loadout.Spec SpecOf(Member m) => new Loadout.Spec
        {
            level = Player.instance != null ? Player.instance.level : 1,
            trees = new List<(Skill_Tree, int)> { (Tree(m), TreeRank(m)) },
            gear = Slots.Where(sl => m.Gear.ContainsKey(sl.ToString())).Select(sl => m.Gear[sl.ToString()]).ToList(),
            melee = m.Kind.melee,
        };

        /// <summary>Every bonus the crew member gets on top of its body's weakest profile.</summary>
        internal static (Dictionary<STAT_TYPE, int> stats, Dictionary<DAMAGE_TYPE, int> resists) Bonuses(Member m) => Loadout.Bonuses(SpecOf(m));

        /// <summary>Rebuilds a crew crawler's base stats and weapon from its record.</summary>
        internal static void Apply(Character c, Member m)
        {
            Loadout.Apply(c, SpecOf(m), m.Hp, PlayerBaseHealth);
            c.nameOverride = $"{m.Name} ({m.Kind.name})";
            try { Wear.Dress(c, m); } catch (Exception e) { Plugin.Log.LogWarning("Crew wear: " + e.Message); }
        }

        /// <summary>The crew member's basic attack: a melee strike or a ranged shot with no cooldown and some damage.</summary>
        internal static Ability BasicAttack(Character c, bool melee) => Loadout.BasicAttack(c, melee);

        /// <summary>Re-applies a member's stats in the world after its gear changes.</summary>
        internal static void Refresh(Member m)
        {
            Character c = NpcOf(m);
            if (c == null) return;
            m.Hp = c.GetHealth();
            Apply(c, m);
        }

        // ---- the cost of leading a crew ---------------------------------------------------------------------

        internal const string EffectClass = "SCCL_Crew";
        internal static string PenaltyId => ModEffect.MakeId(EffectClass, "LeadingACrew");
        private static bool registered;

        private static void RegisterEffect()
        {
            if (registered || EffectManager.instance == null) return;
            registered = true;
            ClassRegistry.RegisterEffects(new ClassDef
            {
                id = EffectClass, name = "Crew", tiers = new List<TierDef>(),
                folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Crew).Assembly.Location), "assets"),
                effects = new List<EffectSpec>
                {
                    new EffectSpec
                    {
                        id = "LeadingACrew", name = "Leading a Crew", icon = "crowded.png", duration = -1, maxStacks = 4, debuff = true,
                        description = "A crew splits everyone's attention: you and each crawler are weaker for every other crew member.",
                        damageDealtPerStack = -Mathf.Clamp(Penalty.Value, 0, 30) / 100f,
                        stats = Penalty.Value > 0 ? new Dictionary<string, int> { { "HealthMax", -4 } } : null,
                    },
                },
            }, null);
        }

        /// <summary>
        /// Like Conjoined's Spread Thin, every member of the party carries the penalty, you and each crawler, one stack
        /// per other crew member (so with two crawlers out, all three of you carry two).
        /// </summary>
        internal static void UpdatePenalty()
        {
            Player p = Player.instance;
            if (p == null || p.effects == null || EffectManager.instance == null) return;
            RegisterEffect();
            var crew = On ? Npcs() : new List<Character>();
            int n = crew.Count;
            foreach (Character c in new[] { (Character)p }.Concat(crew))
            {
                Effect e = c.effects.GetEffect(PenaltyId);
                if (n <= 0 || Penalty.Value <= 0) { if (e != null) { c.effects.RemoveEffect(PenaltyId, true); c.CalculateStats(); } continue; }
                if (e == null) { e = EffectManager.instance.ApplyEffect(PenaltyId, c); if (e == null) continue; e.caster = p; }
                (e as ModEffect)?.SetStacks(n);
                c.CalculateStats();
            }
        }

        /// <summary>Your max health without your own Leading a Crew (crew health is based on it, then takes its own penalty).</summary>
        internal static int PlayerBaseHealth()
        {
            Player p = Player.instance;
            if (p == null) return 40;
            var hpMod = ModEffect.Specs.TryGetValue(PenaltyId, out var rt) ? rt.spec.stats : null;
            int per = hpMod != null && hpMod.TryGetValue("HealthMax", out int v) ? v : 0;
            return p.GetHealthMax() - per * Actions.StacksOn(p, PenaltyId);
        }

        /// <summary>Payday, on every new floor: wages for each crawler you can afford, in hiring order.</summary>
        internal static void Payday()
        {
            if (!On || Menu_Inventory.instance == null) return;
            int wage = Wage, paid = 0;
            foreach (Member m in Members)
            {
                if (Menu_Inventory.instance.GetCredits() >= wage)
                {
                    Menu_Inventory.instance.AdjustCredits(-wage);
                    paid += wage;
                    if (m.Missed > 0) Game.LOG($"{m.Name} is paid up again.");
                    m.Missed = 0;
                    continue;
                }
                m.Missed = m.Missed + 1;
                if (m.Missed == 1) { Game.LOG($"<color=#FFC35C>You can't pay {m.Name} ({wage} credits).</color> Miss another payday and they're gone."); continue; }
                if (TestMutiny ?? (Mutiny.Value && Random.value < 0.5f))
                {
                    m.Turning = true;
                    Game.LOG($"<color=#FF5C5C>{m.Name} has had enough of working for nothing.</color>");
                }
                else Leave(m, $"<color=#FF8F5C>{m.Name} walks out on you</color>, and takes the gear you lent them as back pay.");
            }
            if (paid > 0) Game.LOG($"Payday: <color=#FFD966>-{paid} credits</color> in crew wages.");
        }

        /// <summary>Gone for good, gear and all.</summary>
        private static void Leave(Member m, string why)
        {
            Character c = NpcOf(m);
            if (c != null) Remove(c);
            Raw?.Remove(m.d);
            Game.LOG(why);
        }

        /// <summary>Gear carried by crawlers who turned on you: you get it back from their bodies.</summary>
        private static readonly Dictionary<Character, List<object>> turned = new Dictionary<Character, List<object>>();

        /// <summary>An unpaid crawler turns hostile where it stands.</summary>
        internal static void TurnOn(Member m)
        {
            Character c = NpcOf(m);
            Raw?.Remove(m.d);
            if (c == null) { Game.LOG($"<color=#FF8F5C>{m.Name} never shows up again.</color>"); return; }
            turned[c] = m.Gear.Values.ToList();
            c.RemoveTag(CrewTag);
            c.RemoveTag(StampTag);
            c.nameOverride = $"{m.Name} (unpaid)";
            if (c.ai != null) { c.ai.InitTeam(AI.AI_TEAM.Hostile); c.ai.SetTarget(Player.instance); c.ai.ChangeState(AI.AI_STATE.Active); }
            Game.LOG($"<color=#FF5C5C>{m.Name} turns on you!</color> Take your gear back from their body.");
            AudioManager.PlayEvent("event:/UI/UI_Error");
        }

        private static void RecoverGear(Character c)
        {
            if (!turned.TryGetValue(c, out var gear)) return;
            turned.Remove(c);
            foreach (object saved in gear)
            {
                if (!(saved is ItemSaveInfo info)) continue;
                Item it = ItemDatabase.instance.GetItem(info.itemId);
                if (it == null) continue;
                it.Deserialize(saved);
                if (Menu_Inventory.TakeCheck(it)) { Menu_Inventory.Take(it); Game.LOG($"You take back your {it.GetName(false)}."); }
                else { Game.LOG($"No room for your {it.GetName(false)}; it's lost."); UnityEngine.Object.Destroy(it.gameObject); }
            }
        }

        // ---- in the world -------------------------------------------------------------------------------------

        internal static bool IsCrew(Character c) => c != null && c != Player.instance && c.HasTag(CrewTag) && !c.GetIsDead();

        internal static List<Character> Npcs() =>
            AI_Manager.instance == null ? new List<Character>()
            : (Traverse.Create(AI_Manager.instance).Field("ais").GetValue<List<AI>>() ?? new List<AI>())
                .Where(a => a != null).Select(a => a.GetCharacter()).Where(IsCrew).ToList();

        internal static Character NpcOf(Member m) => Npcs().FirstOrDefault(c => c.GetTag(CrewTag) == m.Id && c.GetTag(StampTag) == m.Stamp);

        /// <summary>Brings the crawlers in line with the records: adopt matching ones, drop stale ones, spawn missing.</summary>
        internal static void Sync()
        {
            if (Player.instance == null || AI_Manager.instance == null || Map.instance == null || Raw == null || Conjoined.Swapping) return;
            if (!On) { foreach (Character c in Npcs()) Remove(c); UpdatePenalty(); return; }
            var members = Members;
            string key = MapKey;
            foreach (Character c in Npcs())
            {
                Member m = members.FirstOrDefault(x => x.Id == c.GetTag(CrewTag));
                if (m == null || m.Downed || c.GetTag(StampTag) != m.Stamp || m.MapKey != key) Remove(c);
            }
            foreach (Member m in members.Where(x => !x.Downed))
            {
                Character c = NpcOf(m);
                if (c == null) c = Spawn(m, m.MapKey == key ? m.Pos : null);
                if (c == null) continue;
                m.MapKey = key;
                Apply(c, m);
            }
            foreach (Member m in Members.Where(x => x.Turning)) TurnOn(m);
            UpdatePenalty();
        }

        private static Character Spawn(Member m, float[] at)
        {
            Character prefab = AI_Manager.instance.GetCharacter(m.Kind.prefab);
            if (prefab == null) { Plugin.Log.LogWarning($"Crew: no body '{m.Kind.prefab}' for {m.Name}"); return null; }
            NavNode node = null;
            if (at != null && at.Length == 3) node = Map.instance.GetNearestNavNodeAt(new Vector3(at[0], at[1], at[2]), 0.75f);
            if (node == null || node.IsOccupied()) node = Map.instance.GetClosestNode(Player.instance.GetMover().GetCurrentNavNode(), 4f, validNodesOnly: true);
            if (node == null || node.IsOccupied()) return null;
            m.Stamp = m.Stamp + 1;
            Character c = AI_Manager.instance.Spawn_Character(prefab, node.transform.position, AI.AI_TEAM.Player);
            if (c == null) return null;
            foreach (string t in c.tags.Where(t => t != null && t.tagName != null).Select(t => t.tagName).ToList())
                if (t.StartsWith("NEMESIS") || t == "BOSS" || t == "ELITE" || t == "BOSSHEALTHBAR" || t.StartsWith("KILLSQUAD") || t.StartsWith("RARESPAWN")) c.RemoveTag(t);
            c.SetTag(CrewTag, m.Id);
            c.SetTag(StampTag, m.Stamp);
            c.AddTag("NOXP");
            c.xpValue = 0;
            if (c.ai != null) { c.ai.SetTarget(null); c.ai.ChangeState(AI.AI_STATE.Idle); }
            return c;
        }

        private static void Remove(Character c)
        {
            if (c == null) return;
            AI_Manager.instance.RemoveCharacter(c);
            c.GetMover()?.GetCurrentNavNode()?.ClearOccupant();
            UnityEngine.Object.Destroy(c.gameObject);
        }

        /// <summary>Health and positions into the records (before any save).</summary>
        internal static void Record()
        {
            if (Raw == null) return;
            string key = MapKey;
            foreach (Member m in Members.Where(x => !x.Downed))
            {
                Character c = NpcOf(m);
                if (c == null) continue;
                m.Hp = c.GetHealth();
                Vector3 p = c.transform.position;
                m.Pos = new[] { p.x, p.y, p.z };
                m.MapKey = key;
            }
        }

        /// <summary>A new floor: the fallen get back up at half health.</summary>
        private static void NewFloor()
        {
            Payday();
            foreach (Member m in Members.Where(x => x.Downed)) { m.Downed = false; m.Hp = -2; m.MapKey = ""; }
        }

        private static System.Collections.IEnumerator PenaltySoon()
        {
            yield return null;
            try { UpdatePenalty(); } catch (Exception e) { Plugin.Log.LogError("Crew penalty: " + e); }
        }

        private static System.Collections.IEnumerator SyncSoon()
        {
            yield return null;
            yield return null;
            try
            {
                foreach (Member m in Members.Where(x => x.Hp == -2))
                {
                    Character c = NpcOf(m);
                    if (c != null) c.SetHealth(Math.Max(1, c.GetHealthMax() / 2), c.GetHealthMax());
                }
                Sync();
                foreach (Member m in Members.Where(x => x.Hp == -2))
                {
                    Character c = NpcOf(m);
                    if (c != null) { c.SetHealth(Math.Max(1, c.GetHealthMax() / 2), c.GetHealthMax()); m.Hp = c.GetHealth(); }
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Crew sync: " + e); }
        }

        [HarmonyPatch(typeof(Level), nameof(Level.OnPlayerEntered))]
        private static class Patch_Entered
        {
            private static int lastFloor = -1;
            private static void Postfix()
            {
                if (Conjoined.Swapping) return;
                if (Game.FLOOR != lastFloor) { if (lastFloor >= 0) NewFloor(); lastFloor = Game.FLOOR; }
                Plugin.Instance.StartCoroutine(SyncSoon());
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.SerializeLevel))]
        private static class Patch_Record
        {
            private static void Prefix() { try { Record(); } catch (Exception e) { Plugin.Log.LogError("Crew record: " + e); } }
        }

        /// <summary>A level-up makes the whole crew stronger at once.</summary>
        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.PlayerLevelUp))]
        private static class Patch_LevelUp
        {
            private static void Postfix() { try { foreach (Member m in Members) Refresh(m); } catch (Exception e) { Plugin.Log.LogError("Crew level-up: " + e); } }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_Die
        {
            private static void Prefix(Character __instance)
            {
                if (__instance != null && turned.ContainsKey(__instance)) { RecoverGear(__instance); return; }
                if (!IsCrew(__instance)) return;
                Member m = Members.FirstOrDefault(x => x.Id == __instance.GetTag(CrewTag) && x.Stamp == __instance.GetTag(StampTag));
                if (m == null) return;
                m.Downed = true;
                m.Hp = 0;
                Game.LOG($"<color=#FF8F5C>{m.Name} is down.</color> They'll be back on the next floor.");
                Plugin.Instance.StartCoroutine(PenaltySoon());
            }
        }
    }
}
