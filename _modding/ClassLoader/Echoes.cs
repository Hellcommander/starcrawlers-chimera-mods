using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// What makes each echo different. Every breach rolls its anomalies (shown on the job board before you go in),
    /// and every echo has a sealed vault whose warden carries the override. The vault's grade (1-5) rises with every
    /// hard anomaly and with the danger of the job you take; grade 4 vaults may hold a legendary item and grade 5
    /// vaults always do, behind a warden to match. Anomalies and vault contents are data (SideAreaData): each anomaly
    /// combines parts (lights, hazards, doors, spawns, caches, a timer, pulses, a hunter, an alarm).
    /// </summary>
    internal static class Echoes
    {
        internal static AnomalySpec Get(string id) => SideAreaData.Anomaly(id);

        private static Dictionary<string, object> D => SideAreas.Data;
        internal const string EffectClass = "SCCL_Echoes";
        internal static string EffectId(string name) => ModEffect.MakeId(EffectClass, name);

        // ---- rolling ------------------------------------------------------------------------------------------

        private static bool Allowed(AnomalySpec a, KindSpec k, int floor) =>
            floor >= a.minFloor && floor <= a.maxFloor
            && (a.kinds == null || a.kinds.Count == 0 || a.kinds.Contains(k.id, StringComparer.OrdinalIgnoreCase))
            && (k.exclude == null || !k.exclude.Contains(a.id, StringComparer.OrdinalIgnoreCase))
            && (k.only == null || k.only.Count == 0 || k.only.Contains(a.id, StringComparer.OrdinalIgnoreCase));

        private static bool Clashes(AnomalySpec a, List<string> picked) =>
            picked.Any(p => (a.notWith?.Contains(p, StringComparer.OrdinalIgnoreCase) ?? false) || (Get(p)?.notWith?.Contains(a.id, StringComparer.OrdinalIgnoreCase) ?? false));

        /// <summary>The breach's anomalies on this floor: rolled once, the first time anyone asks.</summary>
        internal static string[] Rolled(int floor)
        {
            var d = D;
            if (d == null) return new string[0];
            if (d.TryGetValue("anoms_" + floor, out object v) && v is string[] s) return s;
            KindSpec kind = SideKinds.KindOf(floor);
            var picked = new List<string>();
            foreach (string f in kind.force ?? new List<string>())
                if (Get(f) != null && !picked.Contains(f)) picked.Add(Get(f).id);

            var odds = kind.anomalyOdds != null && kind.anomalyOdds.Sum() > 0 ? kind.anomalyOdds : SideAreaData.Classic.anomalyOdds;
            int roll = Random.Range(0, odds.Sum()), n = 0;
            while (n < odds.Count - 1 && (roll -= odds[n]) >= 0) n++;
            var pool = SideAreaData.Anomalies.Where(a => a.weight > 0 && Allowed(a, kind, floor) && !picked.Contains(a.id)).ToList();
            for (int i = 0; i < n; i++)
            {
                pool.RemoveAll(a => Clashes(a, picked));
                if (pool.Count == 0) break;
                int total = pool.Sum(a => a.weight), r = Random.Range(0, total);
                AnomalySpec a = pool.First(x => (r -= x.weight) < 0);
                picked.Add(a.id);
                pool.Remove(a);
            }
            foreach (AnomalySpec a in SideAreaData.Anomalies.Where(a => a.extraChance > 0 && Allowed(a, kind, floor) && !picked.Contains(a.id)))
                if (!Clashes(a, picked) && Random.value < a.extraChance) picked.Add(a.id);
            s = picked.ToArray();
            d["anoms_" + floor] = s;
            return s;
        }

        internal static bool Has(string id) => Active.Contains(id);

        private static string[] Active => D != null && D.TryGetValue("echo_anoms", out object v) && v is string[] s ? s : new string[0];

        private static IEnumerable<AnomalySpec> ActiveSpecs => Active.Select(Get).Where(a => a != null);

        /// <summary>1 + every hard anomaly (+ their extra grades) + danger above Challenging + the kind's own, at most 5.</summary>
        internal static int VaultGrade(string[] anoms, int jobTier, KindSpec kind = null) =>
            Mathf.Clamp(1 + anoms.Select(Get).Where(a => a != null).Sum(a => (a.hard ? 1 : 0) + a.vaultGrade) + Math.Max(0, jobTier - 1) + (kind?.vaultGrade ?? 0) + Endless.VaultBonus, 1, 5);

        internal static string GradeText(int g) =>
            g >= 5 ? "<color=#FF9F2E>grade 5: a legendary inside</color>" : g == 4 ? "<color=#C38CFF>grade 4: epics, maybe a legendary</color>"
            : g == 3 ? "<color=#C38CFF>grade 3: an epic</color>" : $"<color=#6FB7FF>grade {g}: rares</color>";

        internal static string AnomalyNames(string[] anoms) =>
            anoms.Length == 0 ? "nothing unusual" : string.Join(", ", anoms.Select(Get).Where(a => a != null)
                .Select(a => $"<color={(a.hard ? "#FF8F5C" : "#FFD966")}>{a.name}</color>"));

        // ---- building the echo --------------------------------------------------------------------------------

        /// <summary>A new echo: the kind's rules, its anomalies and the vault. Before jobs and factions pick their people.</summary>
        internal static void OnEchoBuilt(int origin)
        {
            if (D == null) return;
            string[] anoms = Rolled(origin);
            KindSpec kind = SideKinds.KindOf(origin);
            D["echo_anoms"] = anoms;
            D["echo_turn"] = 0;
            Room arrival = Player.instance.GetCurrentRoom();
            int tier = Contracts.Job?.tier ?? 0;
            try { SideKinds.Prepare(kind, arrival); }
            catch (Exception e) { Plugin.Log.LogError($"Side area kind {kind.id}: " + e); }
            foreach (AnomalySpec a in anoms.Select(Get).Where(a => a != null))
            {
                try { Apply(a, arrival); }
                catch (Exception e) { Plugin.Log.LogError($"Echo anomaly {a.id}: {e}"); }
            }
            int grade = VaultGrade(anoms, tier, kind);
            if (kind.vault && SideLayers.VaultHere)
            {
                try { BuildVault(arrival, grade); }
                catch (Exception e) { Plugin.Log.LogError("Echo vault: " + e); }
            }
            try { SideKinds.Finish(kind, arrival); }
            catch (Exception e) { Plugin.Log.LogError($"Side area kind {kind.id}: " + e); }
            Game.LOG(anoms.Length > 0 ? $"<color=#D070FF>This {kind.name.ToLowerInvariant()}:</color> {AnomalyNames(anoms)}." : $"<color=#D070FF>This {kind.name.ToLowerInvariant()} seems quiet.</color>");
            foreach (AnomalySpec x in anoms.Select(Get).Where(a => a != null)) Game.LOG($"<color=#9FB7C9>{x.name}: {x.text}</color>");
            Plugin.Log.LogInfo($"Echoes: {kind.id}, anomalies {string.Join(",", anoms)}, vault {(kind.vault ? "grade " + grade : "none")}");
        }

        private static void Apply(AnomalySpec a, Room arrival)
        {
            if (a.thin > 0) SideKinds.Thin(a.thin, arrival);
            if (a.lights != null) Darken(a.lights);
            if (!string.IsNullOrEmpty(a.playerEffect)) Fx(Player.instance, a.playerEffect);
            if (!string.IsNullOrEmpty(a.enemyEffect))
                foreach (Character c in Enemies().Where(_ => Random.value < a.enemyChance).ToList()) Fx(c, a.enemyEffect, a.enemyStacks);
            if (a.hazards != null)
            {
                HazardRoomsSpec h = a.hazards;
                int min = h.rooms != null && h.rooms.Length > 0 ? h.rooms[0] : 3, max = h.rooms != null && h.rooms.Length > 1 ? h.rooms[1] : min;
                foreach (Room r in Rooms(arrival).OrderBy(_ => Random.value).Take(Random.Range(min, max + 1)))
                {
                    NavNode n = r.GetRandomNode();
                    if (n == null) continue;
                    var tiles = h.spread ? new[] { n }.Concat(n.GetLinks().Where(l => l != null && !l.HasDoor())) : new[] { n };
                    foreach (NavNode t in tiles) Hazard(t, h.id, h.turns);
                }
            }
            if (a.doors != null)
                foreach (Door d in Map.instance.GetRooms().Where(r => r != null && r != arrival).SelectMany(r => r.roomDoors).Where(x => x != null).Distinct().ToList())
                {
                    if (d.isSealed || d.isBroken || arrival.roomDoors.Contains(d) || Random.value > a.doors.@lock) continue;
                    d.lockpickMod += a.doors.pick;
                    d.bashMod += a.doors.bash;
                    d.Lock();
                }
            if (a.spawn != null) Spawn(a.spawn, arrival);
            if (a.caches != null)
                for (int i = 0; i < a.caches.count; i++) Cache(arrival, SideAreaData.Rarity(a.caches.rarity), a.caches.items, a.caches.type);
            if (a.timer != null) D["timer_" + a.id] = a.timer.turns;
        }

        internal static IEnumerable<Room> Rooms(Room avoid) =>
            Map.instance.GetRooms().Where(r => r != null && r != avoid && !r.Properties.IsSecret && !r.Properties.IsBossRoom && !(r is Room_Event) && r.RandomNodeCheck());

        internal static IEnumerable<Character> Enemies() =>
            Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Hostile && !c.HasTag("BOSS"));

        internal static void Fx(Character c, string effect, int stacks = 1)
        {
            string id = SideAreaData.EffectId(effect);
            if (c == null || id == null) return;
            Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = id, stacks = stacks },
                new ActionContext { caster = Player.instance, classId = EffectClass, sourceName = "Echo" }, c);
        }

        internal static void Hazard(NavNode n, string id, int turns)
        {
            if (n == null || HazardManager.instance == null || n.HasHazard(id)) return;
            // a teleport pad is a hazard too, and another hazard on its tile clears it: never cover a breach,
            // a way out, a rift or an objective pad
            if (n.hazards.Any(x => x is TeleportPad)) return;
            global::Hazard h = HazardManager.instance.GetHazard(id, n.transform.position);
            if (h == null) return;
            h.SetNode(n);
            h.Init();
            if (Traverse.Create(h).Field("duration").FieldExists()) Traverse.Create(h).Field("duration").SetValue(turns);
        }

        /// <summary>The scene's ambient light before a blackout dimmed it, and what the blackout set.</summary>
        private static Color litAmbient, darkAmbient;
        private static float litIntensity, darkIntensity;
        private static bool dimmed;

        /// <summary>A blackout never takes ambient light below this (brightest channel): dark, but you can still see.</summary>
        private const float DarkestAmbient = 0.11f;

        /// <summary>
        /// Most of the map's lights go out (re-applied after a reload: lights aren't saved). The ambient light is
        /// dimmed from its undimmed value: the game never resets it, so multiplying what's there made every reload,
        /// tower layer and later blackout darker than the last, until nothing could be seen, even back on real floors.
        /// </summary>
        private static void Darken(LightsSpec spec)
        {
            if (Map.instance == null) return;
            int keepEvery = spec.off >= 1f ? int.MaxValue : Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(0.01f, 1f - spec.off)));
            int i = 0;
            foreach (Light l in Map.instance.GetComponentsInChildren<Light>(true))
                if (l != null && l.type != LightType.Directional && l.name != Places.KeepLit && (i++ % keepEvery) != 0) l.enabled = false;
            if (!dimmed || RenderSettings.ambientLight != darkAmbient) litAmbient = RenderSettings.ambientLight;
            if (!dimmed || !Mathf.Approximately(RenderSettings.ambientIntensity, darkIntensity)) litIntensity = RenderSettings.ambientIntensity;
            Color dark = litAmbient * spec.ambient;
            float top = Mathf.Max(dark.r, Mathf.Max(dark.g, dark.b)), litTop = Mathf.Max(litAmbient.r, Mathf.Max(litAmbient.g, litAmbient.b));
            float floor = Mathf.Min(DarkestAmbient, litTop); // never brighter than it was
            if (top < floor) dark = top > 0.0001f ? dark * (floor / top) : new Color(floor, floor, floor, 1f);
            dark.a = litAmbient.a;
            darkAmbient = dark;
            darkIntensity = litIntensity * Mathf.Max(spec.ambient, 0.5f);
            RenderSettings.ambientLight = darkAmbient;
            RenderSettings.ambientIntensity = darkIntensity;
            dimmed = true;
        }

        /// <summary>Leaving: the ambient light the blackout dimmed comes back (unless something has set it since).</summary>
        private static void Undarken()
        {
            if (!dimmed) return;
            dimmed = false;
            if (RenderSettings.ambientLight == darkAmbient) RenderSettings.ambientLight = litAmbient;
            if (Mathf.Approximately(RenderSettings.ambientIntensity, darkIntensity)) RenderSettings.ambientIntensity = litIntensity;
        }

        /// <summary>Extra enemies around the echo (never in the room you arrive in).</summary>
        internal static List<Character> Spawn(SpawnSpec s, Room avoid, bool nearPlayer = false)
        {
            var made = new List<Character>();
            List<Character> prefabs;
            if (s.fromEcho)
                prefabs = Enemies().Where(c => !c.HasTag("SCCLE_WARDEN") && c.internalName != null).Select(c => c.internalName).Distinct()
                    .Select(id => AI_Manager.instance.GetCharacter(id)).Where(p => p != null).ToList();
            else
            {
                List<string> ids = s.ids;
                if ((ids == null || ids.Count == 0) && s.pools != null && s.pools.Count > 0)
                    ids = s.pools[Mathf.Clamp(Game.FLOOR / Math.Max(1, s.poolStep), 0, s.pools.Count - 1)];
                prefabs = (ids ?? new List<string>()).Select(EnemyRegistry.Resolve).Where(p => p != null).ToList();
            }
            if (prefabs.Count == 0) prefabs = new[] { "CORPSEC_GUARD_PISTOL", "CORPSEC_GUARD_MELEE" }.Select(id => AI_Manager.instance.GetCharacter(id)).Where(p => p != null).ToList();
            if (prefabs.Count == 0) return made;
            var rooms = Rooms(avoid).ToList();
            if (nearPlayer)
                rooms = rooms.OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).Take(Math.Max(1, rooms.Count / 3)).ToList();
            int n = s.count + Math.Min(s.maxExtra, s.perFloor * Math.Max(0, Game.FLOOR));
            for (int i = 0, tries = 0; made.Count < n && rooms.Count > 0 && tries < n * 4; tries++)
            {
                Room r = rooms[Random.Range(0, rooms.Count)];
                NavNode node = r.GetRandomNode();
                if (node == null || node.IsOccupied() || node.HasDoor()) continue;
                Character c = AI_Manager.instance.Spawn_Character(prefabs[Random.Range(0, prefabs.Count)], node.transform.position,
                    Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f), AI.AI_TEAM.Hostile);
                if (c == null) continue;
                if (nearPlayer) EnemyAbilities.FacePlayer(c);
                if (s.health != 1f) { int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * s.health)); c.SetHealth(hp, hp); }
                if (!string.IsNullOrEmpty(s.name)) c.SetName(s.name);
                if (!string.IsNullOrEmpty(s.effect)) Fx(c, s.effect, s.effectStacks);
                if (!string.IsNullOrEmpty(s.drop)) c.AddTag(DropTag + SideAreaData.Rarity(s.drop));
                made.Add(c);
                i++;
            }
            return made;
        }

        internal const string DropTag = "SCCLE_DROP_";

        /// <summary>Enemies marked by a spawn, hunter or objective leave their loot when they die.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_Drop
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || __instance.tags == null) return;
                Tag t = __instance.tags.FirstOrDefault(x => x != null && x.tagName != null && x.tagName.StartsWith(DropTag));
                if (t == null) return;
                __instance.RemoveTag(t.tagName);
                if (!Enum.TryParse(t.tagName.Substring(DropTag.Length), out RARITY r)) return;
                Spew(LOOT_TYPE.Random, r, __instance.GetMover()?.GetCurrentNavNode());
            }
        }

        /// <summary>A pile of loot on the floor of a random room.</summary>
        internal static void Cache(Room avoid, RARITY rarity, int count, string type = null)
        {
            Room r = Rooms(avoid).OrderBy(_ => Random.value).FirstOrDefault();
            NavNode n = r?.GetRandomNode();
            if (n == null) return;
            for (int i = 0; i < count; i++)
                Spew(type != null ? SideAreaData.LootType(type) : i % 2 == 0 ? LOOT_TYPE.Random : LOOT_TYPE.Gear, rarity, n);
        }

        internal static void Spew(LOOT_TYPE type, RARITY rarity, NavNode at)
        {
            Item item = LootManager.instance.Generate_Loot(type, rarity);
            if (item == null || at == null) return;
            LootManager.instance.SprinkleLootDust(item);
            Map.instance.AddItem(item);
            item.transform.position = at.transform.position;
            LootManager.instance.DoLootSpew(at.transform.position, item, 1.2f, at.GetRoom(), true, null, 0.3f);
        }

        // ---- the vault ----------------------------------------------------------------------------------------

        internal static int Grade => D != null && D.TryGetValue("vault_grade", out object v) && v is int g ? g : 0;
        internal static bool VaultOpen => D != null && D.TryGetValue("vault_open", out object v) && v is bool b && b;

        internal static Room VaultRoom
        {
            get
            {
                if (D == null || !D.TryGetValue("vault_room", out object v) || !(v is int i) || Map.instance == null) return null;
                var rooms = Map.instance.GetRooms();
                return i >= 0 && i < rooms.Count ? rooms[i] : null;
            }
        }

        /// <summary>The grade's contents from the data, or the built-in table when no mod defines that grade.</summary>
        private static List<(LOOT_TYPE, RARITY)> VaultLoot(int grade)
        {
            if (SideAreaData.Vaults.TryGetValue(grade, out VaultSpec spec))
            {
                var rolled = spec.loot.SelectMany(l => l.Roll()).ToList();
                if (spec.credits) rolled.Add((LOOT_TYPE.Credits, RARITY.None));
                return rolled;
            }
            var loot = new List<(LOOT_TYPE, RARITY)>();
            switch (grade)
            {
                case 1: loot.Add((LOOT_TYPE.Random, RARITY.Rare)); loot.Add((LOOT_TYPE.Gear, RARITY.Rare)); break;
                case 2: loot.Add((LOOT_TYPE.Weapon, RARITY.Rare)); loot.Add((LOOT_TYPE.Gear, RARITY.Rare)); if (Random.value < 0.3f) loot.Add((LOOT_TYPE.Random, RARITY.Epic)); break;
                case 3: loot.Add((LOOT_TYPE.Weapon, RARITY.Epic)); loot.Add((LOOT_TYPE.Gear, RARITY.Rare)); loot.Add((LOOT_TYPE.Random, RARITY.Rare)); break;
                case 4:
                    loot.Add((LOOT_TYPE.Weapon, RARITY.Epic)); loot.Add((LOOT_TYPE.Gear, RARITY.Epic)); loot.Add((LOOT_TYPE.Random, RARITY.Rare));
                    if (Random.value < 0.35f) loot.Add((Random.value < 0.5f ? LOOT_TYPE.Weapon : LOOT_TYPE.Gear, RARITY.Legendary));
                    break;
                default:
                    loot.Add((Random.value < 0.5f ? LOOT_TYPE.Weapon : LOOT_TYPE.Gear, RARITY.Legendary)); loot.Add((LOOT_TYPE.Weapon, RARITY.Epic));
                    loot.Add((LOOT_TYPE.Gear, RARITY.Epic)); loot.Add((LOOT_TYPE.Random, RARITY.Rare));
                    break;
            }
            loot.Add((LOOT_TYPE.Credits, RARITY.None));
            return loot;
        }

        /// <summary>
        /// The room furthest from where you arrive becomes the vault: its doors are sealed, the loot is inside, and
        /// a warden (a much tougher copy of the floor's worst enemy, with grade - 1 veteran guards) stands outside
        /// with the override. Kill the warden and the seals release.
        /// </summary>
        private static void BuildVault(Room arrival, int grade)
        {
            var rooms = Map.instance.GetRooms();
            Room vault = Rooms(arrival).Where(r => !r.Properties.IsHallway && r.roomDoors.Any(d => d != null) && !r.roomDoors.Any(d => arrival.roomDoors.Contains(d)))
                .OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).FirstOrDefault();
            if (vault == null) return;
            D["vault_room"] = rooms.IndexOf(vault);
            D["vault_grade"] = grade;
            D["vault_open"] = false;

            // what's inside
            foreach (Character c in AI_Manager.instance.GetMobsInRoom(vault).ToList()) c.ai.ChangeState(AI.AI_STATE.Idle);
            foreach (var (t, r) in VaultLoot(grade)) Spew(t, r, vault.GetRandomNode());

            foreach (Door d in vault.roomDoors) if (d != null) { if (d.isOpen) d.Close(null); d.SetSeal(true); }

            // the warden, outside
            Room post = vault.linkedRooms.Where(r => r != null && r != arrival && r.RandomNodeCheck()).OrderBy(_ => Random.value).FirstOrDefault()
                        ?? Rooms(arrival).Where(r => r != vault).OrderBy(r => Vector3.Distance(r.transform.position, vault.transform.position)).FirstOrDefault();
            NavNode spot = post?.GetRandomNode();
            if (spot == null || spot.IsOccupied() || spot.HasDoor()) spot = SideAreas.RandomFreeNode(arrival);
            Character template = Enemies().Where(c => c.GetMover()?.GetCurrentNavNode()?.GetRoom() != vault).OrderByDescending(c => c.GetHealthMax()).FirstOrDefault();
            Character prefab = template != null ? AI_Manager.instance.GetCharacter(template.internalName) : AI_Manager.instance.GetCharacter("CORPSEC_COMMANDO");
            if (prefab == null || spot == null) { OpenVault("nobody was left to guard it"); return; }
            Character w = AI_Manager.instance.Spawn_Character(prefab, spot.transform.position, AI.AI_TEAM.Hostile);
            if (w == null) { OpenVault("nobody was left to guard it"); return; }
            Vector3 fromVault = w.transform.position - vault.transform.position; fromVault.y = 0f;
            if (fromVault.sqrMagnitude > 0.01f) w.transform.rotation = Quaternion.LookRotation(fromVault); // back to the vault door
            int hp = w.GetHealthMax() * (2 + grade);
            w.SetHealth(hp, hp);
            w.SetName(grade >= 5 ? "Vault Warden Prime" : "Vault Warden");
            w.AddTag("SCCLE_WARDEN");
            Fx(w, "VaultWarden", grade);
            foreach (Character g in Enemies().Where(c => c != w && c.GetMover()?.GetCurrentNavNode()?.GetRoom() != vault)
                         .OrderBy(c => Vector3.Distance(c.transform.position, w.transform.position)).Take(grade - 1).ToList())
            {
                g.SetName("Vault guard");
                Fx(g, "Garrison");
            }
        }

        private static void OpenVault(string why)
        {
            Room vault = VaultRoom;
            if (vault != null) foreach (Door d in vault.roomDoors) if (d != null) d.SetSeal(false);
            if (D != null) D["vault_open"] = true;
            Game.LOG($"<color=#FFD966>The vault's seals release{(string.IsNullOrEmpty(why) ? "" : ": " + why)}.</color>");
            AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
            BreachFinder.Refresh();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_WardenDown
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || !__instance.HasTag("SCCLE_WARDEN")) return;
                __instance.RemoveTag("SCCLE_WARDEN");
                if (SideAreas.InSide && !VaultOpen) OpenVault("the warden's override went dead with them");
            }
        }

        // ---- reload, turns, leaving ---------------------------------------------------------------------------

        /// <summary>A reloaded echo: lights aren't saved, so a blackout goes dark again; a vault whose warden is gone opens.</summary>
        internal static void OnEchoRestored()
        {
            foreach (AnomalySpec a in ActiveSpecs.Where(a => a.lights != null)) Darken(a.lights);
            Plugin.Instance.StartCoroutine(CheckVaultSoon());
        }

        private static IEnumerator CheckVaultSoon()
        {
            yield return new WaitForSeconds(0.5f);
            if (SideAreas.InSide && Grade > 0 && !VaultOpen && !Contracts.AllCharacters().Any(c => !c.GetIsDead() && c.HasTag("SCCLE_WARDEN")))
                OpenVault(null);
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                if (!SideAreas.InSide || D == null || Game.Game_isLoading) return;
                try { Turn(); } catch (Exception e) { Plugin.Log.LogError("Echo turn: " + e); }
                try { SideKinds.Turn(); } catch (Exception e) { Plugin.Log.LogError("Side area turn: " + e); }
                try { SideLayers.Turn(); } catch (Exception e) { Plugin.Log.LogError("Tower turn: " + e); }
            }
        }

        private static int Counter(string key) => D.TryGetValue(key, out object v) && v is int i ? i : 0;

        private static void Turn()
        {
            int turn = Counter("echo_turn") + 1;
            D["echo_turn"] = turn;
            bool fighting = LevelManager.instance != null && LevelManager.instance.IsInCombat();
            foreach (AnomalySpec a in ActiveSpecs.ToList())
            {
                if (a.timer != null && D.TryGetValue("timer_" + a.id, out object v) && v is int left)
                {
                    left--;
                    D["timer_" + a.id] = left;
                    if (a.timer.warn != null && a.timer.warn.Contains(left)) Game.LOG($"<color=#FF8F5C>{string.Format(a.timer.warnText, left)}</color>");
                    if (left <= 0)
                    {
                        D.Remove("timer_" + a.id);
                        Player p = Player.instance;
                        if (a.timer.damage > 0) p.DamageHealth(Mathf.Max(1, Mathf.RoundToInt(p.GetHealthMax() * a.timer.damage)), null);
                        Game.LOG($"<color=#FF5C5C>{a.timer.endText}</color>");
                        Plugin.Instance.StartCoroutine(SideAreas.Return(collapse: true));
                        return;
                    }
                }
                if (a.pulse != null && Random.value < a.pulse.chance)
                {
                    NavNode here = Player.instance.GetMover()?.GetCurrentNavNode();
                    if (here != null)
                    {
                        var spots = here.GetLinks().Where(l => l != null && !l.HasDoor()).OrderBy(_ => Random.value).Take(a.pulse.tiles).ToList();
                        if (Random.value < a.pulse.onYou) spots.Add(here);
                        foreach (NavNode n in spots) Hazard(n, a.pulse.hazard, a.pulse.turns);
                        if (!string.IsNullOrEmpty(a.pulse.text)) Game.LOG($"<color={a.pulse.color}>{a.pulse.text}</color>");
                    }
                }
                if (a.hunter != null && turn == a.hunter.after && !D.ContainsKey("hunted_" + a.id))
                {
                    D["hunted_" + a.id] = true;
                    Hunter(a.hunter);
                }
                if (a.alarm != null)
                {
                    string onKey = "alarm_on_" + a.id, wavesKey = "alarm_waves_" + a.id;
                    if (!D.ContainsKey(onKey) && (!a.alarm.whenSpotted || fighting) && turn >= a.alarm.start)
                    {
                        D[onKey] = turn;
                        Game.LOG($"<color=#FF5C5C>An alarm starts to wail somewhere in the echo.</color>");
                    }
                    if (D.TryGetValue(onKey, out object on) && on is int since && Counter(wavesKey) < a.alarm.waves && (turn - since) % Math.Max(1, a.alarm.every) == a.alarm.every - 1)
                    {
                        D[wavesKey] = Counter(wavesKey) + 1;
                        var wave = Spawn(new SpawnSpec { fromEcho = true, count = a.alarm.size + Game.FLOOR / 3, perFloor = 0, name = a.alarm.name, effect = a.alarm.effect },
                            Player.instance.GetCurrentRoom(), nearPlayer: true);
                        foreach (Character c in wave) Hunt(c);
                        Game.LOG($"<color=#FF8F5C>{a.alarm.text} ({wave.Count})</color>");
                    }
                }
            }
        }

        internal static void Hunt(Character c)
        {
            if (c?.ai == null) return;
            c.ai.ChangeState(AI.AI_STATE.Active);
            c.ai.SetTarget(Player.instance);
        }

        /// <summary>A much tougher copy of the echo's worst enemy, somewhere away from you, already on your trail.</summary>
        internal static Character Hunter(HunterSpec h)
        {
            Character template = Enemies().Where(c => !c.HasTag("SCCLE_WARDEN")).OrderByDescending(c => c.GetHealthMax()).FirstOrDefault();
            Character prefab = AI_Manager.instance.GetCharacter(template?.internalName ?? "CORPSEC_COMMANDO") ?? AI_Manager.instance.GetCharacter("CORPSEC_COMMANDO");
            Room here = Player.instance.GetCurrentRoom();
            Room far = Rooms(here).OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).Take(3).OrderBy(_ => Random.value).FirstOrDefault();
            NavNode spot = far?.GetRandomNode();
            if (spot == null || spot.IsOccupied() || spot.HasDoor()) spot = SideAreas.RandomFreeNode(here);
            if (prefab == null || spot == null) return null;
            Character c = AI_Manager.instance.Spawn_Character(prefab, spot.transform.position, AI.AI_TEAM.Hostile);
            if (c == null) return null;
            EnemyAbilities.FacePlayer(c);
            int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * h.health));
            c.SetHealth(hp, hp);
            c.SetName(h.name);
            c.AddTag("SCCLE_HUNTER");
            if (!string.IsNullOrEmpty(h.drop)) c.AddTag(DropTag + SideAreaData.Rarity(h.drop));
            if (!string.IsNullOrEmpty(h.effect)) Fx(c, h.effect, h.effectStacks);
            Hunt(c);
            if (!string.IsNullOrEmpty(h.text)) Game.LOG($"<color=#FF5C5C>{h.text}</color>");
            Menu_MiniMap.instance?.CreateSkullMarkerFor(c);
            return c;
        }

        /// <summary>Riding to another layer of a tower: the layer you arrive on puts its own effects back on you.</summary>
        internal static void ReapplyPlayerEffects()
        {
            foreach (AnomalySpec a in ActiveSpecs.Where(a => !string.IsNullOrEmpty(a.playerEffect))) Fx(Player.instance, a.playerEffect);
            Player.instance.CalculateStats();
        }

        /// <summary>Leaving: the echo's own effects come off you, and its state is cleared.</summary>
        internal static void OnReturn()
        {
            Undarken();
            Player p = Player.instance;
            if (p != null)
            {
                var mine = new HashSet<string> { "InTheDark", "RichSeam" };
                foreach (AnomalySpec a in ActiveSpecs.Where(a => !string.IsNullOrEmpty(a.playerEffect))) mine.Add(a.playerEffect);
                foreach (string e in mine) { string id = SideAreaData.EffectId(e); if (id != null) p.effects.RemoveEffect(id, true); }
                p.CalculateStats();
            }
            if (D == null) return;
            foreach (string k in D.Keys.Where(k => k.StartsWith("timer_") || k.StartsWith("hunted_") || k.StartsWith("alarm_")).ToList()) D.Remove(k);
            foreach (string k in new[] { "echo_anoms", "echo_turn", "collapse_turns", "vault_room", "vault_grade", "vault_open" }) D.Remove(k);
        }

        internal static string TrackerLine()
        {
            if (!SideAreas.InSide || D == null || !D.TryGetValue("echo_anoms", out object v) || !(v is string[] anoms)) return null;
            var parts = new List<string>();
            if (anoms.Length > 0) parts.Add(AnomalyNames(anoms));
            foreach (AnomalySpec a in ActiveSpecs.Where(a => a.timer != null))
                if (D.TryGetValue("timer_" + a.id, out object t) && t is int left) parts.Add($"<color=#FF8F5C>{a.timer.label} {left}</color>");
            foreach (AnomalySpec a in ActiveSpecs.Where(a => a.alarm != null))
                if (D.ContainsKey("alarm_on_" + a.id)) parts.Add($"<color=#FF5C5C>alarm: wave {Counter("alarm_waves_" + a.id)}/{a.alarm.waves}</color>");
            if (Grade > 0) parts.Add(VaultOpen ? "<color=#FFD966>vault open</color>" : $"vault sealed ({GradeText(Grade)}), the warden has the override");
            return string.Join("  |  ", parts);
        }

        // ---- effects ------------------------------------------------------------------------------------------

        internal static void RegisterEffects()
        {
            var def = new ClassDef
            {
                id = EffectClass, name = "Echoes", tiers = new List<TierDef>(),
                folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Echoes).Assembly.Location), "assets"),
                effects = new List<EffectSpec>
                {
                    new EffectSpec { id = "InTheDark", name = "In the Dark", icon = "in_the_dark.png", duration = -1,
                        description = "The lights are out. Harder to spot, harder to hit.", stats = new Dictionary<string, int> { { "Stealth", 20 }, { "Dodge", 6 } } },
                    new EffectSpec { id = "BlindSpots", name = "Blind Spots", icon = "in_the_dark.png", duration = -1, debuff = true,
                        description = "Can't see much in the dark.", stats = new Dictionary<string, int> { { "RangedAccuracy", -10 } } },
                    new EffectSpec { id = "RichSeam", name = "Rich Seam", icon = "rich_seam.png", duration = -1,
                        description = "Everything in this echo is worth taking.", stats = new Dictionary<string, int> { { "LootFind", 25 } } },
                    new EffectSpec { id = "Garrison", name = "Veteran", icon = "garrison.png", duration = -1,
                        description = "Doubled shift, and they've seen your kind before.", damageDealtMult = 1.2f, damageTakenMult = 0.85f },
                    new EffectSpec { id = "VaultWarden", name = "Vault Warden", icon = "vault_warden.png", duration = -1, maxStacks = 5,
                        description = "Carries the vault override. The better the vault, the worse the warden.", damageDealtPerStack = 0.1f, damageTakenPerStack = -0.06f },
                },
            };
            ClassRegistry.RegisterEffects(def, null);
        }
    }
}
