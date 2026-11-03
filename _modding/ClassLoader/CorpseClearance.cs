using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A corpse keeps its whole body where it fell, and anyone who walks over it stands inside it: two meshes in one
    /// place z-fight and flicker. With [Interface] HideCorpsesUnderfoot (on) a corpse's look is hidden while a living
    /// body overlaps it and comes back when they step off. Only the look: the corpse is still there to loot, butcher,
    /// raise or eat. The game's own culling hides corpses the same way, so the two are kept in step (Patch_Cull).
    /// </summary>
    internal static class CorpseClearance
    {
        internal static ConfigEntry<bool> Enabled;

        private sealed class State
        {
            public Renderer[] looks;
            /// <summary>Its outline on the floor (x, z), measured while it's drawn: a corpse lies across more than its own tile.</summary>
            public Vector2 min, max;
            public bool measured, culled, under;
        }

        private static readonly Dictionary<Corpse, State> states = new Dictionary<Corpse, State>();
        private static readonly List<(Vector3 at, float r)> bodies = new List<(Vector3, float)>();
        /// <summary>Each living body's footprint radius, from its own mesh (a Hellmutt is wider than a trooper).</summary>
        private static readonly Dictionary<Character, float> radius = new Dictionary<Character, float>();
        private static readonly List<Corpse> gone = new List<Corpse>();

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Interface", "HideCorpsesUnderfoot", true,
                "A corpse is hidden while someone stands in it (no two bodies drawn inside each other) and shows again when they step off. Only the look: it can still be looted, butchered or used.");
        }

        private static State Of(Corpse k)
        {
            if (states.TryGetValue(k, out State s)) return s;
            // what's drawn now, plus the skin the culling switches (parts the model keeps off stay off)
            SkinnedMeshRenderer skin = k.GetComponentInChildren<SkinnedMeshRenderer>(true);
            s = new State { looks = k.GetComponentsInChildren<Renderer>(true).Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && (r.enabled || r == skin)).ToArray() };
            states[k] = s;
            return s;
        }

        /// <summary>Re-measures the outline while the corpse is drawn (it may still be falling when first seen).</summary>
        private static void Measure(State s)
        {
            bool any = false;
            Bounds b = new Bounds();
            foreach (Renderer r in s.looks)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            // a little inside its box, so brushing the tip of a limb doesn't hide it
            Vector3 lo = b.min, hi = b.max, pad = (hi - lo) * 0.12f;
            s.min = new Vector2(lo.x + pad.x, lo.z + pad.z);
            s.max = new Vector2(hi.x - pad.x, hi.z - pad.z);
            s.measured = true;
        }

        private static float Radius(Character c)
        {
            if (radius.TryGetValue(c, out float r)) return r;
            bool any = false;
            Bounds b = new Bounds();
            foreach (SkinnedMeshRenderer m in c.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (m == null || !m.enabled) continue;
                if (!any) { b = m.bounds; any = true; } else b.Encapsulate(m.bounds);
            }
            r = any ? Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * 0.8f, 0.2f, 0.7f) : 0.3f;
            radius[c] = r;
            return r;
        }

        private static void Apply(State s)
        {
            bool show = !s.culled && !s.under;
            foreach (Renderer r in s.looks) if (r != null && r.enabled != show) r.enabled = show;
        }

        /// <summary>Once a frame: who stands in which corpse. Corpses and bodies are few, so this is a plain overlap check.</summary>
        internal static void Tick()
        {
            if (Enabled == null || AI_Manager.instance == null || Player.instance == null || Game.Game_isLoading) return;
            List<Corpse> all = Corpses.All();
            if (all.Count == 0 && states.Count == 0) return;
            bodies.Clear();
            foreach (Character c in Contracts.AllCharacters())
                if (c != null && !c.GetIsDead() && c.gameObject.activeInHierarchy && c != Player.instance) bodies.Add((c.transform.position, Radius(c)));
            foreach (Corpse k in all)
            {
                if (k == null || k.isDissolving) continue;
                State s = Of(k);
                if (!s.culled && !s.under) Measure(s);
                bool under = false;
                if (Enabled.Value && s.measured)
                    foreach (var (p, rad) in bodies)
                    {
                        // the body's footprint (a circle) touching the corpse's outline (a box)
                        float dx = Mathf.Max(s.min.x - p.x, 0f, p.x - s.max.x), dz = Mathf.Max(s.min.y - p.z, 0f, p.z - s.max.y);
                        if (dx * dx + dz * dz < rad * rad) { under = true; break; }
                    }
                if (under == s.under) continue;
                s.under = under;
                Apply(s);
            }
            // corpses eaten, raised or melted away
            gone.Clear();
            foreach (Corpse k in states.Keys) if (k == null || k.isDissolving) gone.Add(k);
            foreach (Character c in radius.Keys.Where(c => c == null || c.GetIsDead()).ToList()) radius.Remove(c);
            foreach (Corpse k in gone)
            {
                if (k != null && states[k].under) { states[k].under = false; Apply(states[k]); }
                states.Remove(k);
            }
        }

        /// <summary>The game's culling (rooms out of sight) shows and hides a corpse too: one doesn't undo the other.</summary>
        [HarmonyPatch(typeof(Corpse), nameof(Corpse.SetCull))]
        private static class Patch_Cull
        {
            private static void Postfix(Corpse __instance, bool flag)
            {
                if (__instance == null) return;
                State s = Of(__instance);
                s.culled = flag;
                Apply(s);
            }
        }
    }
}
