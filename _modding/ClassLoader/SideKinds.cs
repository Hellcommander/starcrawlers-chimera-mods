using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Kinds of side area (SideAreaData.Kinds): each breach rolls one when it appears. A kind decides which floor's
    /// maps the echo borrows, how many anomalies it rolls, who is still inside (strip / thin / toughness), its caches,
    /// a vendor, rifts to deeper layers, and its own objective:
    /// - uplinks: stand on N uplink pads for a few turns each; every finished uplink brings a response team.
    /// - holdout: step on the beacon to start it, then survive the waves; the exits stay shut until it's over.
    /// - bounty: a named, much tougher target with an escort, somewhere far from where you arrive.
    /// Objectives pay the moment they're done; leaving early just forfeits the pay.
    /// </summary>
    internal static class SideKinds
    {
        private static Dictionary<string, object> D => SideAreas.Data;

        private static T Get<T>(string key, T fallback = default) =>
            D != null && D.TryGetValue(key, out object v) && v is T t ? t : fallback;

        // ---- which kind ---------------------------------------------------------------------------------------

        /// <summary>The kind of this floor's breach (a breach from before kinds existed is a classic echo).</summary>
        internal static KindSpec KindOf(int floor) => SideAreaData.Kind(Get<string>("kind_" + floor) ?? "echo");

        /// <summary>The kind of the side area you're in.</summary>
        internal static KindSpec Current => SideAreaData.Kind(Get<string>("echo_kind") ?? "echo");

        internal static KindSpec Roll(int floor)
        {
            var pool = SideAreaData.Kinds.Where(k => k.weight > 0 && floor >= k.minFloor && floor <= k.maxFloor).ToList();
            KindSpec pick = SideAreaData.Classic;
            if (pool.Count > 0)
            {
                int r = Random.Range(0, pool.Sum(k => k.weight));
                pick = pool.First(k => (r -= k.weight) < 0);
            }
            if (D != null) D["kind_" + floor] = pick.id;
            var rosters = SideAreaData.Rosters.Where(r => r.weight > 0 && floor >= r.minFloor && floor <= r.maxFloor
                && (pick.rosters == null || pick.rosters.Count == 0 || pick.rosters.Contains(r.id, StringComparer.OrdinalIgnoreCase))).ToList();
            if (D != null && rosters.Count > 0)
            {
                int r = Random.Range(0, rosters.Sum(x => x.weight));
                D["roster_" + floor] = rosters.First(x => (r -= x.weight) < 0).id;
            }
            return pick;
        }

        /// <summary>Who holds this floor's side area (null = only the borrowed floor's own enemies).</summary>
        internal static RosterSpec RosterOf(int floor) => SideAreaData.Roster(Get<string>("roster_" + floor));

        internal static string RosterText(int floor)
        {
            RosterSpec r = RosterOf(floor);
            return r == null ? null : $"<color={r.color}>{r.name}</color>";
        }

        internal static string Tinted(KindSpec k) => $"<color={k.color}>{k.name}</color>";

        internal static string Article(KindSpec k)
        {
            string n = k.name.ToLowerInvariant();
            return ("aeiou".IndexOf(n[0]) >= 0 ? "an " : "a ") + $"<color={k.color}>{n}</color>";
        }

        // ---- building -----------------------------------------------------------------------------------------

        /// <summary>Before anomalies and the vault: who is still inside.</summary>
        internal static void Prepare(KindSpec k, Room arrival)
        {
            if (k.strip != null && k.strip.Count > 0)
                foreach (Character c in Echoes.Enemies().Where(c => k.strip.Contains(c.characterType.ToString(), StringComparer.OrdinalIgnoreCase)).ToList()) Remove(c);
            if (k.thin > 0) Thin(k.thin, arrival);
            RosterSpec roster = RosterOf(Game.FLOOR);
            if (roster != null && k.replace > 0) Repopulate(roster, k.replace, arrival);
            if (k.toughness != 1f)
                foreach (Character c in Echoes.Enemies().ToList())
                {
                    int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * k.toughness));
                    c.SetHealth(hp, hp);
                }
        }

        /// <summary>Some of the echo's own enemies are gone (never the warden or anyone a job or faction marked).</summary>
        internal static void Thin(float fraction, Room arrival)
        {
            foreach (Character c in Echoes.Enemies().Where(c => c.GetMover()?.GetCurrentNavNode()?.GetRoom() != arrival && !c.HasTag("SCCLE_WARDEN")
                                                                 && !c.tags.Any(t => t?.tagName != null && t.tagName.StartsWith("SCCL"))).ToList())
                if (Random.value < fraction) Remove(c);
        }

        /// <summary>Swaps some of the echo's enemies for the roster's, on the same spots (the first game's per-theme mob charts).</summary>
        private static void Repopulate(RosterSpec roster, float fraction, Room arrival)
        {
            int swapped = 0;
            foreach (Character c in Echoes.Enemies().Where(c => c.GetMover()?.GetCurrentNavNode()?.GetRoom() != arrival
                                                                 && !c.tags.Any(t => t?.tagName != null && t.tagName.StartsWith("SCCL"))).ToList())
            {
                if (Random.value >= fraction) continue;
                var band = roster.Band(Game.FLOOR);
                Character prefab = band.Count == 0 ? null : EnemyRegistry.Resolve(band[Random.Range(0, band.Count)]);
                NavNode node = c.GetMover()?.GetCurrentNavNode();
                if (prefab == null || node == null) continue;
                Vector3 pos = node.transform.position;
                Quaternion rot = c.transform.rotation;
                Remove(c);
                if (AI_Manager.instance.Spawn_Character(prefab, pos, rot, AI.AI_TEAM.Hostile) != null) swapped++; // keeps the spot's facing
            }
            Plugin.Log.LogInfo($"Side areas: {roster.id} took {swapped} spots");
            if (!string.IsNullOrEmpty(roster.text)) Game.LOG($"<color={roster.color}>{roster.name}</color><color=#9FB7C9>: {roster.text}</color>");
        }

        // ---- elites and rivals --------------------------------------------------------------------------------

        internal const string EliteTag = "SCCLK_ELITE", RivalTag = "SCCLK_RIVAL";

        private static bool Special(Character c) =>
            c.HasTag("SCCLE_WARDEN") || c.HasTag(BountyTag) || c.HasTag(RivalTag) || c.HasTag(EliteTag) || c.HasTag("SCCLK_VENDOR") || c.HasTag("SCCLE_HUNTER");

        private static AffixSpec PickAffix(ICollection<string> not = null)
        {
            var pool = SideAreaData.Affixes.Where(a => a.weight > 0 && Game.FLOOR >= a.minFloor && (not == null || !not.Contains(a.id))).ToList();
            if (pool.Count == 0) return null;
            int r = Random.Range(0, pool.Sum(a => a.weight));
            return pool.First(a => (r -= a.weight) < 0);
        }

        private static void Affix(Character c, AffixSpec a, bool rename)
        {
            if (a.health != 1f) { int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * a.health)); c.SetHealth(hp, hp); }
            if (!string.IsNullOrEmpty(a.effect)) Echoes.Fx(c, a.effect);
            if (rename && !string.IsNullOrEmpty(a.name)) c.SetName($"{a.name} {c.GetName()}");
        }

        /// <summary>Some enemies get an affix: a name prefix, its effect, more health and maybe a drop.</summary>
        internal static int MakeElites(float chance)
        {
            int n = 0;
            foreach (Character c in Echoes.Enemies().Where(c => !Special(c)).ToList())
            {
                if (Random.value >= chance) continue;
                AffixSpec a = PickAffix();
                if (a == null) break;
                Affix(c, a, rename: true);
                c.AddTag(EliteTag);
                if (!string.IsNullOrEmpty(a.drop) && Random.value < a.dropChance) c.AddTag(Echoes.DropTag + SideAreaData.Rarity(a.drop));
                n++;
            }
            return n;
        }

        /// <summary>A named rival crawler (a Nemesis body) with a couple of affixes, working the same echo.</summary>
        internal static Character SpawnRival(Room arrival)
        {
            NemesisSpec s = SideAreaData.Nemesis;
            if (s == null || s.bodies == null || s.bodies.Count == 0) return null;
            Character prefab = EnemyRegistry.Resolve(s.bodies[Random.Range(0, s.bodies.Count)]);
            Room room = Echoes.Rooms(arrival).Where(r => r != Echoes.VaultRoom && !r.Properties.IsHallway)
                .OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).Take(5).OrderBy(_ => Random.value).FirstOrDefault();
            NavNode spot = room?.GetRandomNode();
            if (spot == null || spot.IsOccupied() || spot.HasDoor()) spot = SideAreas.RandomFreeNode(arrival);
            if (prefab == null || spot == null) return null;
            Character c = AI_Manager.instance.Spawn_Character(prefab, spot.transform.position, AI.AI_TEAM.Hostile);
            if (c == null) return null;
            EnemyAbilities.FacePlayer(c);
            string body = c.GetName();
            int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * s.health));
            c.SetHealth(hp, hp);
            var taken = new List<string>();
            for (int i = 0; i < s.affixes; i++)
            {
                AffixSpec a = PickAffix(taken);
                if (a == null) break;
                taken.Add(a.id);
                Affix(c, a, rename: false);
            }
            if (!string.IsNullOrEmpty(s.effect)) Echoes.Fx(c, s.effect);
            string first = s.first.Count > 0 ? s.first[Random.Range(0, s.first.Count)] : "Rival";
            string last = s.last.Count > 0 ? " " + s.last[Random.Range(0, s.last.Count)] : "";
            c.SetName(first + last);
            c.AddTag(RivalTag);
            if (!string.IsNullOrEmpty(s.drop)) c.AddTag(Echoes.DropTag + SideAreaData.Rarity(s.drop));
            Menu_MiniMap.instance?.CreateSkullMarkerFor(c);
            string traits = string.Join(", ", taken.Select(id => (SideAreaData.Affixes.First(a => a.id == id).name ?? id).ToLowerInvariant()));
            Game.LOG($"<color=#E070A0>A rival crawler is working this echo too: {first}{last}, a {body.ToLowerInvariant()}{(traits.Length > 0 ? $" ({traits})" : "")}. They won't share.</color>");
            return c;
        }

        private static void Remove(Character m)
        {
            AI_Manager.instance.RemoveCharacter(m);
            m.xpValue = 0;
            m.AddTag("NOXP");
            m.GetMover().GetCurrentNavNode()?.ClearOccupant();
            Object.Destroy(m.gameObject);
        }

        /// <summary>After the vault: the vendor, the objective, and the kind's own caches.</summary>
        internal static void Finish(KindSpec k, Room arrival)
        {
            // anomalies may have brought people in: a stripped kind stays stripped (faction people and spawns excepted)
            if (k.strip != null && k.strip.Count > 0)
                foreach (Character c in Echoes.Enemies().Where(c => k.strip.Contains(c.characterType.ToString(), StringComparer.OrdinalIgnoreCase) && !Special(c)
                                                                     && !c.tags.Any(t => t?.tagName != null && t.tagName.StartsWith("SCCLF"))).ToList()) Remove(c);
            if (!string.IsNullOrEmpty(k.vendor)) PlaceVendor(k, arrival);
            float elite = (k.eliteChance >= 0 ? k.eliteChance : SideAreas.EliteChance.Value / 100f) + Endless.EliteBonus;
            int elites = elite > 0 ? MakeElites(elite) : 0;
            if (elites > 0) Game.LOG($"<color=#FFC35C>{elites} elite{(elites > 1 ? "s" : "")} in here.</color>");
            NemesisSpec nem = SideAreaData.Nemesis;
            float rival = k.nemesisChance >= 0 ? k.nemesisChance : nem?.chance ?? 0f;
            if (nem != null && Game.FLOOR >= nem.minFloor && Random.value < rival) SpawnRival(arrival);
            if (k.objective != null && !string.IsNullOrEmpty(k.objective.type) && SideLayers.ObjectiveHere) SetUp(k.objective, arrival);
            if (!string.IsNullOrEmpty(k.text)) Game.LOG($"<color=#9FB7C9>{k.name}: {k.text}</color>");
        }

        internal static int CacheItems(int fallback) => Endless.Here ? Endless.CacheItems : SideAreas.InSide && Current.cache >= 0 ? Current.cache : fallback;
        internal static RARITY CacheRarity(RARITY fallback) => Endless.Here ? Endless.CacheRarity : SideAreas.InSide && !string.IsNullOrEmpty(Current.cacheRarity) ? SideAreaData.Rarity(Current.cacheRarity) : fallback;

        // ---- the vendor ---------------------------------------------------------------------------------------

        private static void PlaceVendor(KindSpec k, Room arrival)
        {
            Character prefab = EnemyRegistry.Resolve(k.vendor);
            NavNode spot = arrival?.GetRandomNode();
            for (int i = 0; i < 10 && (spot == null || spot.IsOccupied() || spot.HasDoor() || spot == Player.instance.GetMover().GetCurrentNavNode()); i++) spot = arrival?.GetRandomNode();
            if (prefab == null || spot == null || spot.IsOccupied()) { Plugin.Log.LogWarning($"Side area {k.id}: no room for vendor {k.vendor}"); return; }
            Character v = AI_Manager.instance.Spawn_Character(prefab, spot.transform.position, AI.AI_TEAM.Passive);
            if (v == null) return;
            EnemyAbilities.FacePlayer(v);
            v.AddTag("SCCLK_VENDOR");
            Plugin.Instance.StartCoroutine(StockSoon(v, k));
        }

        /// <summary>A frame later, once the bot has set itself up: its own stock goes, the kind's goes in (once; tagged so a reload keeps it).</summary>
        private static System.Collections.IEnumerator StockSoon(Character c, KindSpec k)
        {
            yield return null;
            yield return null;
            if (c == null || c.HasTag("SCCLK_STOCKED")) yield break;
            Vendor v = c.GetComponentInChildren<Vendor>() ?? c.GetComponent<Vendor>();
            if (v?.vendor_items != null)
            {
                foreach (Item old in v.vendor_items.Where(i => i != null)) Object.Destroy(old.gameObject);
                v.vendor_items.Clear();
            }
            Stock(c, k);
            c.AddTag("SCCLK_STOCKED");
        }

        private static void Stock(Character c, KindSpec k)
        {
            Vendor v = c.GetComponentInChildren<Vendor>() ?? c.GetComponent<Vendor>();
            if (v == null) { Plugin.Log.LogWarning($"Side area {k.id}: {k.vendor} isn't a vendor"); return; }
            if (v.vendor_items == null) v.vendor_items = new List<Item>();
            if (v.vendor_items.Count(i => i != null) > 0) return;
            RARITY best = SideAreaData.Rarity(k.vendorRarity);
            LOOT_TYPE[] types = { LOOT_TYPE.Weapon, LOOT_TYPE.Gear, LOOT_TYPE.Random };
            for (int i = 0; i < k.vendorItems; i++)
            {
                RARITY r = i == 0 ? best : Random.value < 0.4f ? best : (RARITY)Math.Max((int)RARITY.Common, (int)best - 1);
                Item it = LootManager.instance.Generate_Loot(types[i % types.Length], r);
                if (it != null) v.AddToVendor(it);
            }
        }

        // ---- objectives ---------------------------------------------------------------------------------------

        internal class ObjPad : MonoBehaviour { public bool holdout; }

        private const string ObjColor = "#5CE1FF", BountyTag = "SCCLK_BOUNTY", WaveTag = "SCCLK_WAVE";

        private static ObjectiveSpec Obj => SideAreas.InSide && SideLayers.ObjectiveHere ? Current.objective : null;
        internal static bool Done => Get("obj_done", false);

        private static List<Vector3> PadSpots()
        {
            var list = new List<Vector3>();
            if (Get<object>("obj_pads") is float[] p)
                for (int i = 0; i + 2 < p.Length; i += 3) list.Add(new Vector3(p[i], p[i + 1], p[i + 2]));
            return list;
        }

        private static void SavePads(List<Vector3> pads) => D["obj_pads"] = pads.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray();

        private static void SetUp(ObjectiveSpec o, Room arrival)
        {
            D["obj_done"] = false;
            switch (o.type)
            {
                case "uplinks":
                case "holdout":
                    {
                        int n = o.type == "holdout" ? 1 : Math.Max(1, o.count);
                        var rooms = Echoes.Rooms(arrival).Where(r => r != Echoes.VaultRoom && !r.Properties.IsHallway).OrderBy(_ => Random.value).ToList();
                        if (o.type == "holdout") // a room or two away from the arrival point, not across the map
                            rooms = rooms.OrderBy(r => Mathf.Abs(Vector3.Distance(r.transform.position, Player.instance.transform.position) - 20f)).ToList();
                        var pads = new List<Vector3>();
                        foreach (Room r in rooms)
                        {
                            if (pads.Count >= n) break;
                            NavNode node = r.GetRandomNode();
                            if (node == null || node.IsOccupied() || node.HasDoor()) continue;
                            pads.Add(node.transform.position);
                        }
                        SavePads(pads);
                        PlacePads();
                        Game.LOG($"<color={ObjColor}>{o.text ?? (o.type == "holdout" ? "Find the beacon and hold it." : $"Find {pads.Count} uplinks and hold each for {o.turns} turns.")}</color>");
                        break;
                    }
                case "bounty":
                    {
                        Character t = Echoes.Hunter(new HunterSpec { health = Math.Max(2, o.count), name = o.name ?? "Marked target", drop = o.reward, text = null });
                        if (t == null) { D["obj_done"] = true; return; }
                        t.AddTag(BountyTag);
                        if (t.ai != null) t.ai.ChangeState(AI.AI_STATE.Idle); // waits for you, unlike a hunter
                        foreach (Character g in Echoes.Enemies().Where(c => c != t).OrderBy(c => Vector3.Distance(c.transform.position, t.transform.position)).Take(Math.Max(0, o.size)).ToList())
                        {
                            g.SetName("Bodyguard");
                            Echoes.Fx(g, "Garrison");
                        }
                        Game.LOG($"<color={ObjColor}>{o.text ?? $"Somewhere in here: {t.GetName()}. Bring them down."}</color>");
                        break;
                    }
            }
        }

        private static void PlacePads()
        {
            ObjectiveSpec o = Obj;
            if (o == null || Done) return;
            foreach (Vector3 v in PadSpots())
            {
                TeleportPad pad = Contracts.PlacePad(v, ObjColor, o.type == "holdout" ? "SCCL_HoldoutBeacon" : "SCCL_Uplink");
                pad.gameObject.AddComponent<ObjPad>().holdout = o.type == "holdout";
                if (o.model != null) ModelAssets.Spawn(o.model, Current.pack?.folder, v, pad.transform); // goes with the pad
            }
        }

        /// <summary>Standing on a pad is what counts (see Turn); stepping onto the beacon starts a holdout.</summary>
        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_ObjPad
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                ObjPad pad = __instance.GetComponent<ObjPad>();
                if (pad == null) return true;
                if (c == Player.instance && !Game.Game_isLoading && pad.holdout && !D.ContainsKey("obj_started") && !Done)
                {
                    D["obj_started"] = Get("echo_turn", 0);
                    D["obj_waves"] = 0;
                    Game.LOG($"<color=#FF8F5C>The beacon wakes the whole echo. Hold out: {Obj?.waves ?? 3} waves are coming, and the exits are sealed until it's over.</color>");
                    AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
                    Object.Destroy(__instance.gameObject);
                    BreachFinder.Refresh();
                }
                return false;
            }
        }

        internal static void Turn()
        {
            SideAreas.KeepPads();
            ObjectiveSpec o = Obj;
            // an objective pad cleared by a hazard landing on it comes back
            if (o != null && !Done && D != null && !D.ContainsKey("obj_started") && (o.type == "uplinks" || o.type == "holdout"))
            {
                var have = Object.FindObjectsOfType<ObjPad>().Where(x => x != null).Select(x => x.transform.position).ToList();
                foreach (Vector3 v in PadSpots().Where(v => !have.Any(h => Vector3.Distance(h, v) < 0.3f)).ToList())
                {
                    TeleportPad pad = Contracts.PlacePad(v, ObjColor, o.type == "holdout" ? "SCCL_HoldoutBeacon" : "SCCL_Uplink");
                    pad.gameObject.AddComponent<ObjPad>().holdout = o.type == "holdout";
                    if (o.model != null) ModelAssets.Spawn(o.model, Current.pack?.folder, v, pad.transform);
                }
            }
            if (o == null || Done || D == null) return;
            switch (o.type)
            {
                case "uplinks":
                    {
                        var pads = PadSpots();
                        Vector3 me = Player.instance.transform.position;
                        int at = pads.FindIndex(v => Vector3.Distance(v, me) < 0.7f);
                        if (at < 0) { D["obj_hold"] = 0; return; }
                        int hold = Get("obj_hold", 0) + 1;
                        D["obj_hold"] = hold;
                        if (hold < o.turns) { Game.LOG($"<color={ObjColor}>Uplinking... {hold}/{o.turns}</color>"); return; }
                        D["obj_hold"] = 0;
                        pads.RemoveAt(at);
                        SavePads(pads);
                        foreach (ObjPad p in Object.FindObjectsOfType<ObjPad>().Where(p => Vector3.Distance(p.transform.position, me) < 0.7f).ToList()) Object.Destroy(p.gameObject);
                        int doneCount = Get("obj_count", 0) + 1;
                        D["obj_count"] = doneCount;
                        AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
                        Game.LOG($"<color={ObjColor}>Uplink {doneCount}/{doneCount + pads.Count} done.</color>");
                        if (pads.Count == 0) Complete(o);
                        else Responders(o.size, "The uplink trips security: a response team is on its way.");
                        BreachFinder.Refresh();
                        break;
                    }
                case "holdout":
                    {
                        if (!(Get<object>("obj_started") is int since)) return;
                        int turn = Get("echo_turn", 0), waves = Get("obj_waves", 0);
                        if (waves < o.waves && (turn - since - 1) % Math.Max(1, o.every) == 0)
                        {
                            D["obj_waves"] = waves + 1;
                            Responders(o.size + Game.FLOOR / 3, $"Wave {waves + 1}/{o.waves} is coming through.", WaveTag);
                        }
                        else if (waves >= o.waves && !Echoes.Enemies().Any(c => c.HasTag(WaveTag))) Complete(o);
                        break;
                    }
                case "bounty":
                    if (!Contracts.AllCharacters().Any(c => !c.GetIsDead() && c.HasTag(BountyTag))) Complete(o);
                    break;
            }
        }

        private static void Responders(int size, string text, string tag = null)
        {
            var wave = Echoes.Spawn(new SpawnSpec { fromEcho = true, count = Math.Max(1, size), perFloor = 0, name = "Responder" }, Player.instance.GetCurrentRoom(), nearPlayer: true);
            foreach (Character c in wave) { if (tag != null) c.AddTag(tag); Echoes.Hunt(c); }
            Game.LOG($"<color=#FF8F5C>{text} ({wave.Count})</color>");
        }

        private static void Complete(ObjectiveSpec o)
        {
            D["obj_done"] = true;
            int credits = o.credits > 0 ? o.credits + 25 * Math.Max(0, Game.FLOOR) : 0;
            if (credits > 0) Menu_Inventory.instance.AdjustCredits(credits);
            if (o.xp > 0) Factions.GiveXp(o.xp + 10 * Math.Max(0, Game.FLOOR));
            NavNode here = Player.instance.GetMover()?.GetCurrentNavNode();
            if (o.type != "bounty") // the bounty's reward is what it drops
                for (int i = 0; i < o.rewardItems; i++) Echoes.Spew(i % 2 == 0 ? LOOT_TYPE.Weapon : LOOT_TYPE.Gear, SideAreaData.Rarity(o.reward), here);
            string pay = string.Join(", ", new[] { credits > 0 ? $"+{credits} credits" : null, o.xp > 0 ? "XP" : null }.Where(x => x != null));
            Game.LOG($"<color=#9FE08A>{o.done ?? "Objective done."}</color>{(pay.Length > 0 ? $" <color=#FFD966>{pay}</color>" : "")}");
            AudioManager.PlayEvent("event:/Abilities/Hacker/Hacker_Hack");
            SideAreas.Data["obj_total"] = Get("obj_total", 0) + 1;
            BreachFinder.Refresh();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_BountyDown
        {
            private static void Postfix(Character __instance)
            {
                if (__instance == null || !__instance.HasTag(BountyTag) || !SideAreas.InSide) return;
                __instance.RemoveTag(BountyTag);
                ObjectiveSpec o = Obj;
                if (o != null && o.type == "bounty" && !Done) Complete(o);
            }
        }

        /// <summary>A running holdout keeps the exits shut.</summary>
        internal static bool ExitsOpen(bool say)
        {
            ObjectiveSpec o = Obj;
            bool open = o == null || o.type != "holdout" || !o.sealExits || Done || !D.ContainsKey("obj_started");
            if (!open && say) Game.LOG("<color=#FF8F5C>The exits are sealed until the holdout is over.</color>");
            return open;
        }

        internal static IEnumerable<Vector3> MarkerSpots()
        {
            if (Obj == null || Done) return Enumerable.Empty<Vector3>();
            if (Obj.type == "bounty")
                return Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.HasTag(BountyTag)).Select(c => c.transform.position).ToList();
            return D.ContainsKey("obj_started") ? Enumerable.Empty<Vector3>() : PadSpots();
        }

        internal static string TrackerLine()
        {
            ObjectiveSpec o = Obj;
            if (o == null || D == null) return null;
            string name = Tinted(Current);
            if (Done) return $"{name}: <color=#9FE08A>objective done</color>";
            switch (o.type)
            {
                case "uplinks": return $"{name}: uplinks {Get("obj_count", 0)}/{Get("obj_count", 0) + PadSpots().Count} (hold each for {o.turns} turns)";
                case "holdout":
                    return D.ContainsKey("obj_started") ? $"{name}: <color=#FF8F5C>holding out, wave {Get("obj_waves", 0)}/{o.waves}</color>{(o.sealExits ? ", exits sealed" : "")}"
                        : $"{name}: find the beacon and hold it";
                case "bounty": return $"{name}: bring down the marked target";
            }
            return null;
        }

        // ---- reload and leaving -------------------------------------------------------------------------------

        internal static void OnEchoRestored()
        {
            PlacePads();
            if (Current.vendor != null)
                foreach (Character c in Contracts.AllCharacters().Where(c => c.HasTag("SCCLK_VENDOR"))) Stock(c, Current);
        }

        internal static void OnLeaveLayer() => OnReturn();

        internal static void OnReturn()
        {
            ObjectiveSpec o = Obj;
            if (o != null && !Done) Game.LOG($"<color=#9FB7C9>You leave the {Current.name.ToLowerInvariant()}'s objective unfinished.</color>");
            if (D == null) return;
            foreach (string k in D.Keys.Where(k => k.StartsWith("obj_") && k != "obj_total").ToList()) D.Remove(k);
        }
    }
}
