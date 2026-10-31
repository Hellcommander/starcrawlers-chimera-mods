using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Traps of your own, and the floor's traps used against enemies.
    ///  - "trap" lays a device on one tile: once armed (armTurns), the first character it affects to step onto it (or
    ///    come within triggerRadius, or stand there when your turn ends) sets it off. Its actions hit that character,
    ///    or with a radius everyone it affects around it. charges = how often it goes off, fuse = it goes off on its own
    ///    after that many turns (a timed mine), turns = it fizzles after that long (0 = stays until used), maxTraps =
    ///    a cap per name (laying one more removes the oldest). Saved with the game (SideAreas.Data "traps"), per floor.
    ///  - "springTraps" sets off your traps (of one name, or all) on this floor right away: a remote detonator.
    ///  - Proc trigger "trap": one of your traps went off ("match" = its name; the victim is who it caught).
    ///  - Vanilla floor traps only go off on your side. An enemy you knock back, pull, throw, warp or swap onto one now
    ///    sets it off too ([Traps] ShoveIntoTraps), and you get the experience if it dies.
    /// </summary>
    internal static class Traps
    {
        internal static ConfigEntry<bool> ShoveInto;

        internal static void Bind(ConfigFile cfg)
        {
            ShoveInto = cfg.Bind("Traps", "ShoveIntoTraps", true,
                "An enemy you knock back, pull, throw, warp or swap onto a live floor trap (pressure plate, tripwire) sets it off.");
        }

        private class Trap
        {
            public ActionSpec spec;
            public string classId, name, map;
            public Vector3 pos;
            public NavNode node;
            public List<NavNode> zone = new List<NavNode>();
            public int arm, life, fuse, charges, firedTick = -1;
            /// <summary>The laying skill's rank, damage multiplier (perks, runes) and chance bonus, kept for when it goes off.</summary>
            public int rank, chanceBonus;
            public float damageMult = 1f;
            public readonly List<GameObject> visuals = new List<GameObject>();
        }

        private static readonly List<Trap> traps = new List<Trap>();
        private static int tick;

        private static string MapKey => $"{Game.FLOOR}|{(Map.instance != null ? Map.instance.internalName : "")}";

        private static IEnumerable<Trap> Here() { string key = MapKey; return traps.Where(t => t.map == key && t.node != null); }

        private static bool Live(Trap t) => t.arm <= 0 && traps.Contains(t);

        // ---- laying ---------------------------------------------------------------------------------------------

        internal static void Lay(ActionSpec a, ActionContext ctx, Character t)
        {
            if (Map.instance == null) return;
            // At the aim point, once per cast (an area ability runs its actions per enemy); "to" someone = under them.
            string to = (a.to ?? "").ToLowerInvariant();
            bool aimed = (to == "" || to == "target") && ctx.center.HasValue;
            if (aimed)
            {
                if (ctx.groundLaid == null) ctx.groundLaid = new HashSet<ActionSpec>();
                if (!ctx.groundLaid.Add(a)) return;
            }
            Vector3 at = aimed ? ctx.center.Value
                       : t != null && t.GetMover()?.GetCurrentNavNode() != null ? t.GetMover().GetCurrentNavNode().transform.position
                       : ctx.caster.transform.position;
            NavNode node = Map.instance.GetNearestNavNodeAt(at, 1f);
            if (node == null) return;
            string name = a.fieldName ?? ctx.sourceName ?? "Trap";

            foreach (Trap old in Here().Where(o => o.node == node && Same(o.name, name)).ToList()) Remove(old);
            if (a.maxTraps > 0)
                foreach (Trap old in Here().Where(o => Same(o.name, name)).Reverse().Skip(a.maxTraps - 1).ToList()) Remove(old);

            var tr = new Trap
            {
                spec = a, classId = ctx.classId, name = name, map = MapKey, node = node, pos = node.transform.position,
                arm = Math.Max(0, a.armTurns), life = Math.Max(0, a.turns), fuse = Math.Max(0, a.fuse), charges = a.charges,
                rank = ctx.rank, damageMult = ctx.damageMult, chanceBonus = ctx.chanceBonus,
            };
            Place(tr);
            traps.Add(tr);
            string who = Same(ctx.sourceName, name) ? "" : $"{ctx.sourceName}: ";
            Game.LOG($"{who}{name} set{(tr.arm > 0 ? $", live in {tr.arm} turn{(tr.arm > 1 ? "s" : "")}" : "")}{(tr.fuse > 0 ? $"; it goes off in {tr.fuse} turns" : "")}.");
            Record();
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Its trigger zone and its look (a mine's look when the spec names none).</summary>
        private static void Place(Trap tr)
        {
            tr.zone = tr.spec.triggerRadius > 0f
                ? Map.instance.GetNavNodesAt(tr.pos, tr.spec.triggerRadius).Where(n => n != null).ToList()
                : new List<NavNode> { tr.node };
            if (!tr.zone.Contains(tr.node)) tr.zone.Add(tr.node);
            ActionSpec look = tr.spec;
            if (look.model == null && string.IsNullOrEmpty(look.look))
                look = new ActionSpec { look = DefaultLook };
            tr.visuals.AddRange(GroundFields.Visuals(look, tr.classId, new List<Vector3> { tr.pos }));
        }

        internal const string DefaultLook = "HAZARD_Security_Boobytrap";

        private static void Remove(Trap tr)
        {
            foreach (GameObject v in tr.visuals) if (v != null) Object.Destroy(v);
            tr.visuals.Clear();
            traps.Remove(tr);
        }

        // ---- going off ------------------------------------------------------------------------------------------

        /// <summary>Who a trap catches: everyone it affects in its radius, else whoever set it off (or stands on it).</summary>
        private static List<Character> Caught(Trap tr, Character tripper)
        {
            if (tr.spec.radius > 0f)
                return Map.instance.GetNavNodesAt(tr.pos, tr.spec.radius).Where(n => n != null).SelectMany(TileStacks.On)
                    .Where(c => GroundFields.Affects(tr.spec.affects, c)).Distinct().ToList();
            if (tripper != null) return new List<Character> { tripper };
            return TileStacks.On(tr.node).Where(c => GroundFields.Affects(tr.spec.affects, c)).ToList();
        }

        /// <summary>The names of the last traps to go off (tests).</summary>
        internal static readonly List<string> Sprung = new List<string>();

        private static void GoOff(Trap tr, Character tripper)
        {
            if (!traps.Contains(tr) || tr.firedTick == tick) return;
            tr.firedTick = tick;
            Sprung.Add(tr.name);
            if (Sprung.Count > 40) Sprung.RemoveAt(0);
            var caught = Caught(tr, tripper);
            Actions.SpawnVfx(tr.spec.vfx, tr.pos + Vector3.up * 0.1f);
            ModAudio.Play(tr.spec.sound, tr.pos, tr.classId);
            Character first = tripper ?? caught.FirstOrDefault();
            Game.LOG(first != null ? $"<color=#FFB35C>{tr.name}</color> goes off under {first.GetName()}!" : $"<color=#FFB35C>{tr.name}</color> goes off.");

            var ctx = new ActionContext
            {
                caster = Player.instance, classId = tr.classId, sourceName = tr.name, center = tr.pos, groundAtCenter = true,
                rank = tr.rank, damageMult = tr.damageMult, chanceBonus = tr.chanceBonus,
            };
            var acts = tr.spec.actions ?? new List<ActionSpec>();
            var self = acts.Where(x => (x.to ?? "").ToLowerInvariant() == "self").ToList();
            var rest = acts.Except(self).ToList();
            foreach (Character c in caught)
                if (c != null && !c.GetIsDead()) { ctx.target = c; Actions.Run(rest, ctx, c); }
            Actions.Run(self, ctx, Player.instance);
            ProcHub.Fire("trap", first, tr.name);

            if (tr.spec.charges > 0 && --tr.charges <= 0) Remove(tr);
            Record();
        }

        /// <summary>"springTraps": your traps of that name (all of them with none) on this floor go off now.</summary>
        internal static void Spring(ActionSpec a)
        {
            foreach (Trap tr in Here().Where(t => string.IsNullOrEmpty(a.fieldName) || Same(t.name, a.fieldName)).ToList())
                GoOff(tr, null);
        }

        private static void Tick()
        {
            if (traps.Count == 0) return;
            tick++;
            foreach (Trap tr in Here().ToList())
            {
                if (tr.arm > 0) tr.arm--;
                if (tr.fuse > 0 && --tr.fuse == 0) { GoOff(tr, null); if (traps.Contains(tr)) Remove(tr); continue; }
                if (tr.life > 0 && --tr.life == 0) { Game.LOG($"{tr.name} fizzles out."); Remove(tr); continue; }
                if (!Live(tr)) continue;
                // Someone standing in it when your turn ends (it was laid under them, or they stopped there).
                Character c = tr.zone.SelectMany(TileStacks.On).FirstOrDefault(x => GroundFields.Affects(tr.spec.affects, x));
                if (c != null) GoOff(tr, c);
            }
            Record();
        }

        internal static int CountHere(string name) => Here().Count(t => string.IsNullOrEmpty(name) || Same(t.name, name));

        /// <summary>For the devtest: pieces of look on the floor for this floor's traps.</summary>
        internal static int ShownHere() => Here().Sum(t => t.visuals.Count(v => v != null));

        /// <summary>A live trap of yours that would go off on an ally stepping here (it affects "all").</summary>
        internal static bool HurtsAllies(NavNode n) =>
            n != null && traps.Count > 0 && Here().Any(t => (t.spec.affects ?? "").ToLowerInvariant() == "all" && t.zone.Contains(n));

        internal static void ClearAll()
        {
            foreach (Trap tr in traps.ToList()) Remove(tr);
            Record();
        }

        // ---- saving ---------------------------------------------------------------------------------------------

        private static void Record()
        {
            var data = SideAreas.Data;
            if (data == null) return;
            data["traps"] = traps.Select(t => new Dictionary<string, object>
            {
                ["map"] = t.map, ["class"] = t.classId ?? "", ["name"] = t.name, ["spec"] = JsonConvert.SerializeObject(t.spec),
                ["arm"] = t.arm, ["life"] = t.life, ["fuse"] = t.fuse, ["charges"] = t.charges,
                ["rank"] = t.rank, ["mult"] = t.damageMult, ["chance"] = t.chanceBonus,
                ["pos"] = new[] { t.pos.x, t.pos.y, t.pos.z },
            }).ToList();
        }

        private static IEnumerator RestoreSoon()
        {
            yield return null;
            while (Game.Game_isLoading || Map.instance == null) yield return null;
            try { Restore(); }
            catch (Exception e) { Plugin.Log.LogError("Traps restore: " + e); }
        }

        private static void Restore()
        {
            foreach (Trap tr in traps.ToList()) Remove(tr);
            var data = SideAreas.Data;
            if (data == null || !(data.TryGetValue("traps", out object v) && v is List<Dictionary<string, object>> list)) return;
            foreach (var d in list)
            {
                float[] p = d["pos"] as float[] ?? new float[3];
                var tr = new Trap
                {
                    map = d["map"] as string, classId = d["class"] as string, name = d["name"] as string,
                    spec = JsonConvert.DeserializeObject<ActionSpec>((string)d["spec"]),
                    arm = (int)d["arm"], life = (int)d["life"], fuse = (int)d["fuse"], charges = (int)d["charges"],
                    pos = new Vector3(p[0], p[1], p[2]),
                    rank = d.TryGetValue("rank", out object r) ? (int)r : 0,
                    damageMult = d.TryGetValue("mult", out object m) ? (float)m : 1f,
                    chanceBonus = d.TryGetValue("chance", out object cb) ? (int)cb : 0,
                };
                // Other floors' traps wait (unplaced) for you to come back.
                if (tr.map == MapKey)
                {
                    tr.node = Map.instance.GetNearestNavNodeAt(tr.pos, 0.5f);
                    if (tr.node == null) continue;
                    Place(tr);
                }
                traps.Add(tr);
            }
        }

        // ---- patches --------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() { try { Tick(); } catch (Exception e) { Plugin.Log.LogError("Traps: " + e); } }
        }

        [HarmonyPatch(typeof(NavNode), nameof(NavNode.OnEnterNode))]
        private static class Patch_Enter
        {
            private static void Postfix(NavNode __instance, Character c)
            {
                if (traps.Count == 0 || c == null || Game.Game_isLoading) return;
                if (Here().Any(t => t.zone.Contains(__instance))) Plugin.Instance.StartCoroutine(EnterSoon(__instance, c));
            }
        }

        /// <summary>A frame later, as for fields: a creature being spawned gets its team after it is placed.</summary>
        private static IEnumerator EnterSoon(NavNode n, Character c)
        {
            yield return null;
            if (c == null || c.GetIsDead() || c.GetMover()?.GetCurrentNavNode() != n) yield break;
            foreach (Trap tr in Here().Where(t => Live(t) && t.zone.Contains(n)).ToList())
            {
                if (!GroundFields.Affects(tr.spec.affects, c)) continue;
                try { GoOff(tr, c); } catch (Exception e) { Plugin.Log.LogError("Trap: " + e); }
                if (c == null || c.GetIsDead()) yield break;
            }
        }

        [HarmonyPatch(typeof(Level), nameof(Level.OnPlayerEntered))]
        private static class Patch_Level
        {
            private static void Postfix() => Plugin.Instance.StartCoroutine(RestoreSoon());
        }

        // ---- enemies shoved onto the floor's traps --------------------------------------------------------------

        /// <summary>Characters moved by force just now (Time.time), so the trap under where they land treats them as fair game.</summary>
        private static readonly Dictionary<Character, float> shoved = new Dictionary<Character, float>();
        private static readonly HashSet<(int, int)> told = new HashSet<(int, int)>();

        private static void Mark(Character c)
        {
            if (c == null || c == Player.instance) return;
            if (shoved.Count > 50) foreach (var k in shoved.Keys.Where(k => k == null || Time.time - shoved[k] > 5f).ToList()) shoved.Remove(k);
            shoved[c] = Time.time;
        }

        /// <summary>A hostile creature you moved by force in the last few seconds (a knockback plays out over a moment).</summary>
        internal static bool Shoved(Character c) =>
            ShoveInto.Value && c != null && !c.GetIsDead() && c.ai != null && c.ai.GetBaseTeam() == AI.AI_TEAM.Hostile
            && !c.HasTag("FLYING") && shoved.TryGetValue(c, out float at) && Time.time - at < 4f;

        [HarmonyPatch(typeof(Ability), nameof(Ability.Knockback))]
        private static class Patch_Knockback { private static void Prefix(Character victim) => Mark(victim); }

        [HarmonyPatch(typeof(Actions), "Pull")]
        private static class Patch_Pull { private static void Prefix(Character t) => Mark(t); }

        [HarmonyPatch(typeof(Moves), nameof(Moves.Throw))]
        private static class Patch_Throw { private static void Prefix(Character t) => Mark(t); }

        [HarmonyPatch(typeof(Moves), nameof(Moves.Warp))]
        private static class Patch_Warp { private static void Prefix(Character t) => Mark(t); }

        [HarmonyPatch(typeof(Afflictions), nameof(Afflictions.Swap))]
        private static class Patch_Swap { private static void Prefix(Character t) => Mark(t); }

        /// <summary>Vanilla spares hostile creatures; one you shoved is fair game (cameras keep their own rule).</summary>
        [HarmonyPatch(typeof(Trap_Trigger), "CanUseOn")]
        private static class Patch_CanUse
        {
            private static void Postfix(Trap_Trigger __instance, Character c, ref bool __result)
            {
                if (__result || Game.Game_isLoading || __instance.GetTripped() || !Shoved(c)) return;
                __result = true;
                if (told.Add((__instance.GetInstanceID(), c.GetInstanceID())))
                    Game.LOG($"<color=#9FE08A>{c.GetName()} is driven onto the {(string)__instance.hazardName}!</color>");
            }
        }

        /// <summary>With Reflexes' perk a pressure plate you step on disarms itself; one an enemy is shoved onto still fires.</summary>
        [HarmonyPatch(typeof(Trap_Trigger), nameof(Trap_Trigger.OnEnterNode))]
        private static class Patch_Plate
        {
            private static bool Prefix(Trap_Trigger __instance, Character c)
            {
                if (__instance.internalName != "TRIGGER_TRAP_Pressure_Plate" || __instance.GetTripped() || !Shoved(c)) return true;
                if (told.Add((__instance.GetInstanceID(), c.GetInstanceID())))
                    Game.LOG($"<color=#9FE08A>{c.GetName()} is driven onto the {(string)__instance.hazardName}!</color>");
                __instance.Trigger(c, false);
                return false;
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.Init))]
        private static class Patch_NewMap
        {
            private static void Postfix() { shoved.Clear(); told.Clear(); }
        }
    }
}
