using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Ground that stays: "hazard" lays real vanilla hazards (fire, acid, ...; the game runs and saves them), "field"
    /// lays a mod field that runs its own actions on whoever stands in it each turn and when they step in, for a number
    /// of turns. Fields can borrow a vanilla hazard's look and pulse a particle each turn. They're kept in the mod's save
    /// slot (SideAreas.Data "fields"), so a save mid-fight keeps them.
    /// </summary>
    internal static class GroundFields
    {
        private class Field
        {
            public ActionSpec spec;
            public string classId, source, map;
            public List<NavNode> nodes = new List<NavNode>();
            public int turns;
            public readonly List<GameObject> visuals = new List<GameObject>();
            public readonly Dictionary<Character, int> lastHit = new Dictionary<Character, int>();
        }

        private static readonly List<Field> fields = new List<Field>();
        private static int tick;
        private static GameObject holder;

        private static string MapKey => $"{Game.FLOOR}|{(Map.instance != null ? Map.instance.internalName : "")}";

        // ---- vanilla hazards ------------------------------------------------------------------------------------

        internal static string HazardName(string id)
        {
            Hazard h = HazardManager.instance?.allHazards?.FirstOrDefault(x => x != null && x.internalName == id);
            string n = h != null ? Traverse.Create(h).Field("hazardName").GetValue()?.ToString() : null;
            return string.IsNullOrEmpty(n) ? (id ?? "a hazard").Replace("HAZARD_", "").Replace("_", " ").ToLowerInvariant() : n;
        }

        /// <summary>
        /// Where ground goes. Tile-sized: under the target. Wide: once per cast, at the ability's aim point when it has
        /// one (an area ability runs its actions per enemy; this keeps one field instead of one per enemy). Null = skip.
        /// </summary>
        private static List<NavNode> Tiles(ActionSpec a, ActionContext ctx, Character t)
        {
            bool wide = a.radius > 0f;
            if (wide && ctx.center.HasValue)
            {
                if (ctx.groundLaid == null) ctx.groundLaid = new HashSet<ActionSpec>();
                if (!ctx.groundLaid.Add(a)) return null;
            }
            Vector3 at = (wide || ctx.groundAtCenter) && ctx.center.HasValue ? ctx.center.Value
                       : t != null && t.GetMover()?.GetCurrentNavNode() != null ? t.GetMover().GetCurrentNavNode().transform.position
                       : ctx.center ?? ctx.caster.transform.position;
            if (a.radius > 0f) return Map.instance.GetNavNodesAt(at, a.radius).Where(n => n != null && !n.BlockCheck()).ToList();
            NavNode one = Map.instance.GetNearestNavNodeAt(at, 1f);
            return one != null ? new List<NavNode> { one } : new List<NavNode>();
        }

        internal static void LayHazard(ActionSpec a, ActionContext ctx, Character t)
        {
            if (HazardManager.instance == null || Map.instance == null || string.IsNullOrEmpty(a.hazard)) return;
            foreach (NavNode n in Tiles(a, ctx, t) ?? new List<NavNode>())
            {
                if (n.HasHazard(a.hazard)) continue;
                Hazard h = HazardManager.instance.GetHazard(a.hazard, n.transform.position);
                if (h == null) { Plugin.Log.LogWarning($"{ctx.sourceName}: hazard '{a.hazard}' not found"); return; }
                h.caster = ctx.caster;
                h.SetNode(n);
                h.Init();
                if (a.turns > 0 && Traverse.Create(h).Field("duration").FieldExists()) Traverse.Create(h).Field("duration").SetValue(a.turns);
            }
        }

        // ---- mod fields -----------------------------------------------------------------------------------------

        internal static void Lay(ActionSpec a, ActionContext ctx, Character t)
        {
            if (Map.instance == null) return;
            List<NavNode> tiles = Tiles(a, ctx, t);
            if (tiles == null || tiles.Count == 0) return;
            var f = new Field { spec = a, classId = ctx.classId, source = ctx.sourceName, map = MapKey, turns = Math.Max(1, a.turns), nodes = tiles };
            fields.Add(f);
            Show(f);
            Game.LOG($"{ctx.sourceName}: {(a.fieldName ?? "a field")} spreads over the floor.");
            Record();
        }

        /// <summary>Hazard looks are made to fill a tile on their own; on every tile of a wide field they pile into a wall.</summary>
        private const float LookSpacing = 1.9f;

        /// <summary>Scripts on a hazard's look that would make a copy act like the hazard, loop its sound, or block the tile.</summary>
        private static readonly HashSet<string> LookScripts = new HashSet<string>
        {
            "Hazard", "Tooltip_Tiny", "NavNode_Blocker", "Ability", "LookAtPlayer", "HighlightEffect", "DestroyAfterTime", "StudioEventEmitter",
        };

        private static void Show(Field f) => f.visuals.AddRange(Visuals(f.spec, f.classId, Spread(f)));

        /// <summary>
        /// Ground's look at these spots, kept until destroyed: its model, a vanilla hazard's look (without the hazard)
        /// or a lasting particle. Shared by fields and traps.
        /// </summary>
        internal static List<GameObject> Visuals(ActionSpec spec, string classId, List<Vector3> spots)
        {
            var made = new List<GameObject>();
            if (spots.Count == 0 || Map.instance == null) return made;
            if (holder == null) { holder = new GameObject("SCCL_Fields"); holder.transform.SetParent(Map.instance.transform, false); }
            float scale = Actions.SplitScale(spec.look, out string lookId);
            Hazard look = string.IsNullOrEmpty(lookId) ? null : HazardManager.instance?.allHazards?.FirstOrDefault(x => x != null && x.internalName == lookId);
            // A mesh on each spot (knives, coins), alongside any look below.
            if (spec.model != null)
                foreach (Vector3 p in spots)
                {
                    GameObject prop = ModelAssets.Spawn(spec.model, ClassRegistry.FolderOf(classId), p, holder.transform);
                    if (prop != null) made.Add(prop);
                }
            // Not a hazard: a particle (effect or ability id) kept playing for as long as the field lasts.
            GameObject particle = look == null && !string.IsNullOrEmpty(lookId) ? Actions.ResolveVfx(lookId) : null;
            if (particle != null)
            {
                foreach (Vector3 p in spots)
                {
                    GameObject v = Object.Instantiate(particle, p + Vector3.up * 0.1f, Quaternion.identity, holder.transform);
                    Actions.Rescale(v, scale);
                    v.SetActive(true);
                    Actions.Wake(particle, v);
                    made.Add(v);
                }
                return made;
            }
            if (look == null)
            {
                foreach (Vector3 p in spots) Actions.SpawnVfx(spec.pulseVfx, p + Vector3.up * 0.1f);
                return made;
            }
            var staging = new GameObject("staging");
            staging.SetActive(false);
            foreach (Vector3 p in spots)
            {
                GameObject v = Object.Instantiate(look.gameObject, p, Quaternion.identity, staging.transform);
                // Only the look: no hazard logic, tooltips, clicks or stacked sounds (the first copy keeps its sound), and no screen-warping glass.
                bool keepSound = p == spots[0];
                foreach (Component c in v.GetComponentsInChildren<Component>(true))
                    if (c is Collider || c is Hazard || (c is MonoBehaviour && LookScripts.Contains(c.GetType().Name) && !(keepSound && c.GetType().Name == "StudioEventEmitter")))
                        Object.DestroyImmediate(c);
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
                    if ((r.sharedMaterial?.shader?.name ?? "").Contains("Distortion")) r.enabled = false;
                Actions.Rescale(v, scale);
                v.transform.SetParent(holder.transform, true);
                made.Add(v);
            }
            Object.Destroy(staging);
            return made;
        }

        /// <summary>
        /// Where a field's look goes: the middle tile outward, skipping tiles close to one already picked, and close to
        /// the same look from another field still on the floor.
        /// </summary>
        private static List<Vector3> Spread(Field f)
        {
            var live = f.nodes.Where(n => n != null).ToList();
            if (live.Count == 0) return new List<Vector3>();
            Vector3 mid = live.Aggregate(Vector3.zero, (s, n) => s + n.transform.position) / live.Count;
            // Another field already showing the same thing on the very same tile (two knives in one spot): show it once.
            var taken = fields.Where(o => o != f && o.spec.look == f.spec.look && o.spec.pulseVfx == f.spec.pulseVfx && o.spec.model?.mesh == f.spec.model?.mesh)
                              .SelectMany(o => o.nodes.Where(n => n != null).Select(n => n.transform.position)).ToList();
            var picked = new List<Vector3>();
            foreach (NavNode n in live.OrderBy(n => (n.transform.position - mid).sqrMagnitude))
            {
                Vector3 p = n.transform.position;
                if (taken.Any(q => (q - p).sqrMagnitude < 0.25f)) continue;
                float gap = f.spec.spacing > 0f ? f.spec.spacing : LookSpacing;
                if (picked.Any(q => (q - p).sqrMagnitude < gap * gap - 0.01f)) continue;
                picked.Add(p);
            }
            return picked;
        }

        private static void Hide(Field f)
        {
            foreach (GameObject v in f.visuals) if (v != null) Object.Destroy(v);
            f.visuals.Clear();
        }

        private static bool Affects(Field f, Character c) => Affects(f.spec.affects, c);

        /// <summary>Whether ground made for "enemies" (default), "allies" or "all" touches this character.</summary>
        internal static bool Affects(string affects, Character c)
        {
            if (c == null || c.GetIsDead()) return false;
            bool ally = c == Player.instance || (c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player);
            switch ((affects ?? "enemies").ToLowerInvariant())
            {
                case "allies": return ally;
                case "all": return true;
                default: return !ally && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Passive;
            }
        }

        private static void Hit(Field f, Character c)
        {
            if (!Affects(f, c)) return;
            if (f.lastHit.TryGetValue(c, out int when) && when == tick) return;
            f.lastHit[c] = tick;
            var live = f.nodes.Where(n => n != null).ToList();
            Vector3 mid = live.Count > 0 ? live.Aggregate(Vector3.zero, (s, n) => s + n.transform.position) / live.Count : c.transform.position;
            var ctx = new ActionContext { caster = Player.instance, classId = f.classId, sourceName = f.spec.fieldName ?? f.source, center = mid };
            Actions.Run(f.spec.actions, ctx, c);
        }

        private static void Tick()
        {
            if (fields.Count == 0) return;
            tick++;
            string key = MapKey;
            foreach (Field f in fields.ToList())
            {
                if (f.map != key) continue;
                if (!string.IsNullOrEmpty(f.spec.pulseVfx))
                    foreach (Vector3 p in Spread(f)) Actions.SpawnVfx(f.spec.pulseVfx, p + Vector3.up * 0.1f);
                foreach (NavNode n in f.nodes.Where(n => n != null))
                    foreach (Character c in TileStacks.On(n).ToList()) Hit(f, c);
                if (--f.turns <= 0) { Hide(f); fields.Remove(f); }
            }
            Record();
        }

        internal static int CountHere() => fields.Count(f => f.map == MapKey);

        internal static int CountHere(string fieldName) => fields.Count(f => f.map == MapKey && string.Equals(f.spec.fieldName, fieldName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Picks up every field of this name on this floor (thrown knives, coins): gone, and their middles returned.</summary>
        internal static List<Vector3> TakeUp(string fieldName)
        {
            string key = MapKey;
            var hit = fields.Where(f => f.map == key && string.Equals(f.spec.fieldName, fieldName, StringComparison.OrdinalIgnoreCase)).ToList();
            var mids = new List<Vector3>();
            foreach (Field f in hit)
            {
                var live = f.nodes.Where(n => n != null).ToList();
                if (live.Count > 0) mids.Add(live.Aggregate(Vector3.zero, (s, n) => s + n.transform.position) / live.Count);
                Hide(f);
                fields.Remove(f);
            }
            if (hit.Count > 0) Record();
            return mids;
        }

        /// <summary>For the look gallery test: what the newest field's look is made of.</summary>
        internal static string DescribeLastLook()
        {
            Field f = fields.LastOrDefault();
            if (f == null || f.visuals.Count == 0) return "no look";
            GameObject v = f.visuals[0];
            return string.Join("; ", v.GetComponentsInChildren<Renderer>(true).Select(r =>
                $"{r.GetType().Name}:{r.name} shader={r.sharedMaterial?.shader?.name} size={r.bounds.size.magnitude:0.0} on={r.enabled && r.gameObject.activeInHierarchy}"))
                + " | scripts: " + string.Join(",", v.GetComponentsInChildren<MonoBehaviour>(true).Select(m => m.GetType().Name).Distinct());
        }

        internal static void ClearAll()
        {
            foreach (Field f in fields) Hide(f);
            fields.Clear();
            Record();
        }

        // ---- saving ---------------------------------------------------------------------------------------------

        private static void Record()
        {
            var data = SideAreas.Data;
            if (data == null) return;
            data["fields"] = fields.Select(f => new Dictionary<string, object>
            {
                ["map"] = f.map, ["turns"] = f.turns, ["class"] = f.classId ?? "", ["src"] = f.source ?? "",
                ["spec"] = JsonConvert.SerializeObject(f.spec),
                ["pos"] = f.nodes.Where(n => n != null).SelectMany(n => new[] { n.transform.position.x, n.transform.position.y, n.transform.position.z }).ToArray(),
            }).ToList();
        }

        private static IEnumerator RestoreSoon()
        {
            yield return null;
            while (Game.Game_isLoading || Map.instance == null) yield return null;
            try { Restore(); }
            catch (Exception e) { Plugin.Log.LogError("Ground fields restore: " + e); }
        }

        private static void Restore()
        {
            foreach (Field f in fields) Hide(f);
            fields.Clear();
            holder = null;
            var data = SideAreas.Data;
            if (data == null || !(data.TryGetValue("fields", out object v) && v is List<Dictionary<string, object>> list)) return;
            foreach (var d in list)
            {
                var f = new Field
                {
                    map = d["map"] as string, turns = (int)d["turns"], classId = d["class"] as string, source = d["src"] as string,
                    spec = JsonConvert.DeserializeObject<ActionSpec>((string)d["spec"]),
                };
                float[] p = d["pos"] as float[] ?? new float[0];
                if (f.map == MapKey)
                    for (int i = 0; i + 2 < p.Length; i += 3)
                    {
                        NavNode n = Map.instance.GetNearestNavNodeAt(new Vector3(p[i], p[i + 1], p[i + 2]), 0.5f);
                        if (n != null) f.nodes.Add(n);
                    }
                fields.Add(f);
                if (f.map == MapKey && f.nodes.Count > 0) Show(f);
            }
        }

        // ---- patches --------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() { try { Tick(); } catch (Exception e) { Plugin.Log.LogError("Ground fields: " + e); } }
        }

        [HarmonyPatch(typeof(NavNode), nameof(NavNode.OnEnterNode))]
        private static class Patch_Enter
        {
            private static void Postfix(NavNode __instance, Character c)
            {
                if (fields.Count == 0 || c == null || Game.Game_isLoading) return;
                if (fields.Any(f => f.spec.onEnter && f.nodes.Contains(__instance))) Plugin.Instance.StartCoroutine(EnterSoon(__instance, c));
            }
        }

        /// <summary>
        /// A frame later: a creature being spawned is placed on its tile before it gets its team, so a summon appearing
        /// in an enemy-only field would otherwise be hit as an enemy.
        /// </summary>
        private static IEnumerator EnterSoon(NavNode n, Character c)
        {
            yield return null;
            if (c == null || c.GetIsDead() || c.GetMover()?.GetCurrentNavNode() != n) yield break;
            foreach (Field f in fields.ToList())
                if (f.spec.onEnter && f.nodes.Contains(n)) { try { Hit(f, c); } catch (Exception e) { Plugin.Log.LogError("Ground field: " + e); } }
        }

        [HarmonyPatch(typeof(Level), nameof(Level.OnPlayerEntered))]
        private static class Patch_Level
        {
            private static void Postfix() => Plugin.Instance.StartCoroutine(RestoreSoon());
        }
    }
}
