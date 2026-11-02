using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Keeps out of video memory the textures nothing is using. The game is one scene that references every floor, so all of its
    /// textures are in video memory from the first menu on, and none of them are built for Unity's mip streaming. Each
    /// sweep looks at what the current floor and everything active uses, the event bus's workers decide what can go, and
    /// the rest is unloaded a slice at a time.
    ///
    /// Unity reads an unloaded texture back when a material is asked for it, but a material keeps drawing with the binding
    /// it had, which is black, until it's given the texture again. So anything new is rebound as it's made, before its first
    /// frame (Rebind), and a look over the active renderers each second catches what was switched on or re-dressed.
    /// Each unload starts a new generation, so a material already rebound since the last one costs nothing.
    ///
    /// UI art is never touched: there is little to save in it, and Unity's UI doesn't go through the renderers a sweep can see.
    /// </summary>
    internal static class AssetStream
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> KeepUnusedSeconds, SkipSmallerThanKB;
        internal static ConfigEntry<string> NeverUnload;

        private const float SweepSeconds = 20f;
        private const int ScanSliceMs = 1, UnloadSliceMs = 1;

        internal static bool On => TestOn || (EventBus.On && Enabled != null && Enabled.Value);
        /// <summary>Self-tests: on, and nothing held back, without touching the player's settings.</summary>
        internal static bool TestOn;

        internal static int Sweeps, Unloaded, LastDropped, LastLive;
        internal static long LastFreedBytes;
        internal static double LastDecideMs;

        internal static int Rebound;
        internal static double SlowestLookMs, LookMsTotal;
        internal static int Looks;

        private enum Stage { Idle, Scan, Watch, Deciding, Unload }

        private struct Entry { public int id; public long bytes; public string name; }
        private class Plan { public int[] drop; public long bytes; public int waiting; public double ms; }

        private static Stage stage;
        private static float nextSweep;
        private static int lastFloor = int.MinValue;
        private static bool wasLoading, kept;
        private static bool sweepAsked, hooked;
        private static float nextLook;
        private static int generation;
        private const float LookSeconds = 1f;
        private const int WholeBelow = 16;
        private static readonly Stack<Transform> walking = new Stack<Transform>();
        private static readonly List<Renderer> found = new List<Renderer>();
        private static readonly Dictionary<int, int> bound = new Dictionary<int, int>();
        private static readonly List<Renderer> made = new List<Renderer>();

        private static readonly HashSet<int> keep = new HashSet<int>();      // UI and anything else that must stay; read by the workers while Deciding
        private static readonly HashSet<int> live = new HashSet<int>();      // in use this sweep; also read by the workers
        private static readonly HashSet<int> scanned = new HashSet<int>();
        private static readonly Dictionary<int, Entry> known = new Dictionary<int, Entry>();
        private static readonly Dictionary<int, Texture2D> byId = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<Shader, int[]> slotsOf = new Dictionary<Shader, int[]>();
        private static readonly Dictionary<int, float> lastUsed = new Dictionary<int, float>();   // the workers' own, one plan at a time
        private static readonly List<Material> materials = new List<Material>();
        private static readonly List<int> slots = new List<int>();
        private static Renderer[] scanning;
        private static int scanAt;
        private static int[] dropping;
        private static int dropAt;
        private static long dropBytes;

        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Performance", "AssetStreaming", false,
                "Needs EventBus on. Textures no floor or effect in use needs leave video memory, and Unity reads them back from disk when something draws them. Saves gigabytes of video memory; the first sight of something not seen for a while can cost a short pause. UI art is never unloaded.");
            KeepUnusedSeconds = cfg.Bind("Performance", "AssetStreamingKeepSeconds", 120,
                "Needs AssetStreaming on. A texture stays this long after its last use, so the effects of a fight don't load again each time they come back.");
            SkipSmallerThanKB = cfg.Bind("Performance", "AssetStreamingSkipSmallerThanKB", 512,
                "Needs AssetStreaming on. Textures smaller than this always stay: they save little and would cost a read from disk.");
            NeverUnload = cfg.Bind("Performance", "AssetStreamingNeverUnload", "ui,icon,hud,font,portrait,cursor,button,lut,dirt,flare,noise,ramp,lightmap,reflection",
                "Needs AssetStreaming on. Textures whose name contains one of these words always stay. Sprites, font atlases and anything on a UI element stay whatever this says.");
        }

        /// <summary>Asks for a sweep now rather than at the next timer.</summary>
        internal static void SweepNow() => sweepAsked = true;
        internal static bool Busy => stage != Stage.Idle;

        internal static void Tick()
        {
            if (!On)
            {
                stage = Stage.Idle;
                return;
            }
            if (!hooked) { hooked = true; Hook(); }
            bool loading = Game.Game_isLoading;
            if (loading)
            {
                // what a sweep was looking at may be gone; an unload still queued is put off, not lost
                wasLoading = true;
                stage = Stage.Idle;
                scanning = null;
                return;
            }
            if (wasLoading || Game.FLOOR != lastFloor)
            {
                wasLoading = false;
                lastFloor = Game.FLOOR;
                sweepAsked = true;
            }
            try
            {
                switch (stage)
                {
                    case Stage.Idle:
                        if (sweepAsked || Time.realtimeSinceStartup >= nextSweep) Begin();
                        else if (generation > 0 && Time.realtimeSinceStartup >= nextLook) Look();
                        break;
                    case Stage.Scan:
                        if (Scan()) Hand();
                        break;
                    case Stage.Watch:
                        if (Walk()) stage = Stage.Idle;
                        break;
                    case Stage.Unload:
                        Unload();
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Asset streaming stopped this sweep: " + e);
                stage = Stage.Idle;
            }
        }

        /// <summary>What stays for good, taken before the first unload while every sprite can still be read.</summary>
        private static void KeepUi()
        {
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && s.texture != null) keep.Add(s.texture.GetInstanceID());
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.material != null && f.material.mainTexture != null) keep.Add(f.material.mainTexture.GetInstanceID());
            foreach (TMPro.TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                if (f != null && f.atlasTextures != null)
                    foreach (Texture2D t in f.atlasTextures) if (t != null) keep.Add(t.GetInstanceID());
            foreach (Texture2D t in LightmapSettings.lightmaps.SelectMany(l => new[] { l.lightmapColor, l.lightmapDir, l.shadowMask }))
                if (t != null) keep.Add(t.GetInstanceID());
        }

        private static void Begin()
        {
            sweepAsked = false;
            nextSweep = Time.realtimeSinceStartup + SweepSeconds;
            if (!kept) { kept = true; KeepUi(); }
            foreach (Graphic g in Object.FindObjectsOfType<Graphic>(true))
            {
                // what the canvas already holds: Graphic.material makes a copy on text
                CanvasRenderer c = g.canvasRenderer;
                if (c == null) continue;
                for (int i = 0; i < c.materialCount; i++)
                    if (c.GetMaterial(i) is Material each) Read(each, keep);
                if (g is RawImage raw && raw.texture != null) keep.Add(raw.texture.GetInstanceID());
                if (g is Image image && image.sprite != null && image.sprite.texture != null) keep.Add(image.sprite.texture.GetInstanceID());
            }
            live.Clear();
            scanned.Clear();
            if (RenderSettings.skybox != null) Read(RenderSettings.skybox, live);
            var seen = new List<Renderer>(Object.FindObjectsOfType<Renderer>());
            // the whole floor, the rooms not on show included: reading them now means walking into one costs nothing
            if (Map.instance != null) seen.AddRange(Map.instance.GetComponentsInChildren<Renderer>(true));
            scanning = seen.ToArray();
            scanAt = 0;
            stage = Stage.Scan;
        }

        /// <summary>The second-by-second look: only what's on show, and only what hasn't been rebound since the last unload.</summary>
        private static void Look()
        {
            // down the tree a slice at a time: FindObjectsOfType goes through the whole scene in one call, 26 ms a second
            walking.Clear();
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) walking.Push(root.transform);
            scanned.Clear();
            stage = Stage.Watch;
        }

        /// <summary>A slice of the walk; true when it has reached the end. Only what's active is looked at.</summary>
        private static bool Walk()
        {
            long began = Stopwatch.GetTimestamp(), budget = Stopwatch.Frequency * ScanSliceMs / 1000;
            while (walking.Count > 0 && Stopwatch.GetTimestamp() - began <= budget)
            {
                Transform t = walking.Pop();
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (t.childCount > WholeBelow)
                {
                    t.GetComponents(found);
                    for (int i = 0; i < t.childCount; i++) walking.Push(t.GetChild(i));
                }
                else t.GetComponentsInChildren(found);
                foreach (Renderer r in found)
                {
                    r.GetSharedMaterials(materials);
                    foreach (Material m in materials)
                        if (m != null && scanned.Add(m.GetInstanceID())) Read(m, null);
                }
            }
            double ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
            SlowestLookMs = Math.Max(SlowestLookMs, ms);
            LookMsTotal += ms;
            Looks++;
            if (walking.Count > 0) return false;
            nextLook = Time.realtimeSinceStartup + LookSeconds;
            return true;
        }

        private static void Hook()
        {
            var h = new HarmonyLib.Harmony("arendeth.starcrawlers.classloader.assetstream");
            var after = new HarmonyLib.HarmonyMethod(typeof(AssetStream), nameof(Made));
            // never the generic Instantiate<T>: every reference type shares one body, so a patch typed for one breaks the rest.
            // Nearly all of those end up in these anyway; what doesn't is caught by the look a second later.
            IEnumerable<System.Reflection.MethodBase> targets = typeof(Object).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(x => x.Name == nameof(Object.Instantiate) && !x.IsGenericMethod)
                .Concat(HarmonyLib.AccessTools.GetDeclaredMethods(typeof(AI_Manager)).Where(x => x.Name == nameof(AI_Manager.Spawn_Character) && !x.IsGenericMethod));
            foreach (System.Reflection.MethodBase m in targets)
            {
                try { h.Patch(m, postfix: after); }
                catch (Exception e) { Plugin.Log.LogWarning($"Asset streaming: can't watch {m}: {e.Message}"); }
            }
        }

        /// <summary>Whatever was just made draws for the first time this frame, so its textures are given back here.</summary>
        private static void Made(Object __result)
        {
            if (generation == 0 || !On) return;
            GameObject go = __result as GameObject ?? (__result as Component)?.gameObject;
            if (go == null) return;
            go.GetComponentsInChildren(true, made);
            foreach (Renderer r in made)
            {
                r.GetSharedMaterials(materials);
                foreach (Material m in materials) if (m != null) Read(m, null);
            }
        }

        /// <summary>Asking a material for a texture brings it back if it was unloaded; giving it back makes it draw again.</summary>
        private static void Read(Material m, HashSet<int> into)
        {
            if (m.shader == null) return;
            int mat = m.GetInstanceID();
            bool stale = generation > 0 && (!bound.TryGetValue(mat, out int was) || was != generation);
            if (into == null && !stale) return;
            if (stale) bound[mat] = generation;
            if (!slotsOf.TryGetValue(m.shader, out int[] ids))
            {
                m.GetTexturePropertyNameIDs(slots);
                slotsOf[m.shader] = ids = slots.ToArray();
            }
            foreach (int id in ids)
            {
                if (!m.HasProperty(id)) continue;
                Texture t = m.GetTexture(id);
                if (t == null) continue;
                into?.Add(t.GetInstanceID());
                if (stale && t is Texture2D) { m.SetTexture(id, t); Rebound++; }
            }
        }

        /// <summary>A slice of the renderers; true when they've all been seen.</summary>
        private static bool Scan()
        {
            long began = Stopwatch.GetTimestamp(), budget = Stopwatch.Frequency * ScanSliceMs / 1000;
            HashSet<int> into = stage == Stage.Scan ? live : null;
            while (scanAt < scanning.Length)
            {
                if (Stopwatch.GetTimestamp() - began > budget) return false;
                Renderer r = scanning[scanAt++];
                if (r == null) continue;
                r.GetSharedMaterials(materials);
                foreach (Material m in materials)
                    if (m != null && scanned.Add(m.GetInstanceID())) Read(m, into);
            }
            scanning = null;
            return true;
        }

        /// <summary>Plain data for the workers: from here to the answer nothing of Unity's is touched.</summary>
        private static void Hand()
        {
            byId.Clear();
            var all = new List<Entry>();
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                int id = t.GetInstanceID();
                if (id <= 0) continue;   // made at run time: there is no file to read it back from
                if (!known.TryGetValue(id, out Entry e))
                {
                    e = new Entry { id = id, name = t.name, bytes = Bytes(t) };
                    if (t.isReadable) keep.Add(id);   // something reads these from code, which an unloaded texture would answer as gone
                    known[id] = e;
                }
                byId[id] = t;
                all.Add(e);
            }
            Entry[] entries = all.ToArray();
            float now = Time.realtimeSinceStartup, hold = TestOn ? 0f : KeepUnusedSeconds.Value;
            long least = SkipSmallerThanKB.Value * 1024L;
            string[] never = NeverUnload.Value.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
            LastLive = live.Count;
            stage = Stage.Deciding;
            if (!BusWorkers.Run(() => Decide(entries, now, hold, least, never), Decided)) stage = Stage.Idle;
        }

        /// <summary>A worker. Biggest first, so a sweep cut short has already saved the most.</summary>
        private static Plan Decide(Entry[] entries, float now, float hold, long least, string[] never)
        {
            var clock = Stopwatch.StartNew();
            var drop = new List<Entry>();
            int waiting = 0;
            foreach (Entry e in entries)
            {
                if (live.Contains(e.id)) { lastUsed[e.id] = now; continue; }
                if (keep.Contains(e.id) || e.bytes < least) continue;
                string lower = e.name.ToLowerInvariant();
                if (never.Any(w => lower.Contains(w))) continue;
                if (lastUsed.TryGetValue(e.id, out float at) && now - at < hold) { waiting++; continue; }
                drop.Add(e);
            }
            drop.Sort((a, b) => b.bytes.CompareTo(a.bytes));
            return new Plan { drop = drop.Select(e => e.id).ToArray(), bytes = drop.Sum(e => e.bytes), waiting = waiting, ms = clock.Elapsed.TotalMilliseconds };
        }

        /// <summary>Back on the main thread, from the bus's budget.</summary>
        private static void Decided(Plan p)
        {
            if (stage != Stage.Deciding) return;   // a load came first
            dropping = p.drop;
            dropAt = 0;
            dropBytes = p.bytes;
            LastDecideMs = p.ms;
            LastDropped = p.drop.Length;
            stage = Stage.Unload;
            if (p.drop.Length > 0) Plugin.Log.LogInfo($"Asset streaming: {p.drop.Length} textures to unload, {p.bytes / (1024 * 1024)} MB; {LastLive} in use, {p.waiting} waiting out their {KeepUnusedSeconds.Value}s; decided in {p.ms:0.0} ms on a worker");
        }

        private static void Unload()
        {
            long began = Stopwatch.GetTimestamp(), budget = Stopwatch.Frequency * UnloadSliceMs / 1000;
            while (dropAt < dropping.Length)
            {
                if (Stopwatch.GetTimestamp() - began > budget) return;
                int id = dropping[dropAt++];
                // what became used since the plan comes back by itself the next time it's drawn
                if (byId.TryGetValue(id, out Texture2D t) && t != null) { Resources.UnloadAsset(t); Unloaded++; }
            }
            LastFreedBytes = dropBytes;
            Sweeps++;
            if (dropping.Length > 0) generation++;
            byId.Clear();
            stage = Stage.Idle;
        }

        private static long Bytes(Texture2D t)
        {
            float bpp;
            switch (t.format)
            {
                case TextureFormat.DXT1: case TextureFormat.DXT1Crunched: case TextureFormat.BC4: bpp = 4f; break;
                case TextureFormat.DXT5: case TextureFormat.DXT5Crunched: case TextureFormat.BC5: case TextureFormat.BC6H: case TextureFormat.BC7: bpp = 8f; break;
                case TextureFormat.RGB24: bpp = 24f; break;
                case TextureFormat.RGBAHalf: bpp = 64f; break;
                case TextureFormat.RGBAFloat: bpp = 128f; break;
                default: bpp = 32f; break;
            }
            return (long)(t.width * (long)t.height * bpp / 8f * (t.mipmapCount > 1 ? 1.33f : 1f));
        }
    }
}
