using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// What a big fight piles onto one frame, spread out. Unity allows particles to be made and started on the main thread
    /// only, so that work can't go to the workers: a burst is stretched over the next few frames instead, finished effects
    /// are used again rather than rebuilt, and each system is stripped of what ties it to the main thread, leaving
    /// Unity's own job threads the simulating. Only the look changes, never what a skill does.
    /// </summary>
    internal static class ParticleLoad
    {
        internal static ConfigEntry<int> MaxPerFrame, ParkBeyond;

        private const int MaxWaiting = 256, SweepEvery = 30, SweepSlice = 400, WakeSlice = 256, MaxSeen = 200000, PoolEach = 12, TooLateFrames = 3;
        private const float TooLate = 0.4f;

        internal static int Delayed, Dropped, Reused, Lightened;
        internal static int ParkedNow => parked.Count;

        private struct Waiting { public string spec; public Vector3 pos; public float at; public int frame; }

        private static readonly Queue<Waiting> queue = new Queue<Waiting>();
        private static readonly Dictionary<string, Stack<Actions.VfxLife>> pools = new Dictionary<string, Stack<Actions.VfxLife>>();
        private static readonly HashSet<int> seen = new HashSet<int>();
        private static readonly List<ParticleSystem> parked = new List<ParticleSystem>();
        private static ParticleSystem[] sweep;
        private static int frame = -1, spawned, sweepIn, sweepAt, wakeAt;

        internal static void Init(ConfigFile cfg)
        {
            MaxPerFrame = cfg.Bind("Performance", "MaxEffectSpawnsPerFrame", 6,
                "Needs EventBus on. Most mod particle effects started in one frame. A burst past this plays over the next few frames, so it never lands as one long frame.");
            ParkBeyond = cfg.Bind("Performance", "ParkEffectsBeyond", 20,
                "Needs EventBus on. Looping effects further than this many tiles from you, or in a room you can't see into, stop until you come back, so a floor full of burning, poisoned enemies costs nothing out of sight. 0 = never.");
            ParticleGate.Init(cfg);
        }

        /// <summary>True for a spot the game isn't drawing: past the parking distance, or in a room it has hidden.</summary>
        internal static bool OutOfSight(Vector3 at, Vector3 you, float far)
        {
            if (float.IsInfinity(far)) return false;
            return (at - you).sqrMagnitude > far || NodeIndex.Hidden(Map.instance, at);
        }

        /// <summary>Takes a stopped looping system until it's in sight again.</summary>
        internal static void Park(ParticleSystem ps) => parked.Add(ps);

        private static int Limit => Mathf.Max(1, BusProfile.Shown(MaxPerFrame.Value));

        private static void NewFrame()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            spawned = 0;
        }

        /// <summary>True when this effect has to wait for a later frame.</summary>
        internal static bool Later(string spec, Vector3 pos)
        {
            if (!EventBus.On) return false;
            NewFrame();
            if (spawned < Limit && queue.Count == 0) { spawned++; return false; }
            if (queue.Count >= MaxWaiting) { queue.Dequeue(); Dropped++; } // the newest blast is the one you're looking at
            queue.Enqueue(new Waiting { spec = spec, pos = pos, at = Time.unscaledTime, frame = Time.frameCount });
            Delayed++;
            return true;
        }

        /// <summary>Once a frame, from the bus's pump.</summary>
        internal static void Tick()
        {
            NewFrame();
            while (queue.Count > 0 && spawned < Limit)
            {
                Waiting w = queue.Dequeue();
                // both, because a slow frame alone can be a third of a second: three of them is still only one burst behind
                if (Time.unscaledTime - w.at > TooLate && Time.frameCount - w.frame > TooLateFrames) { Dropped++; continue; }
                if (Actions.SpawnNow(w.spec, w.pos)) spawned++; // one refused cost nothing, so it shouldn't hold the next back
            }
            Vector3 you = Player.instance != null ? Player.instance.transform.position : Vector3.zero;
            float far = Player.instance != null && ParkBeyond.Value > 0 ? ParkBeyond.Value * ParkBeyond.Value : float.PositiveInfinity;
            StatusLook.Tick(you, far);
            WakeParked(you, far);
            if (sweep == null || sweepAt >= sweep.Length)
            {
                if (--sweepIn > 0) return;
                sweepIn = SweepEvery;
                if (seen.Count > MaxSeen) seen.Clear();
                // the game makes its own effects in hundreds of places, so these are caught here
                sweep = Object.FindObjectsOfType<ParticleSystem>();
                sweepAt = 0;
            }
            // thousands of systems at once would be a spike of its own
            for (int end = Mathf.Min(sweepAt + SweepSlice, sweep.Length); sweepAt < end; sweepAt++)
            {
                ParticleSystem ps = sweep[sweepAt];
                if (ps == null) continue;
                if (seen.Add(ps.GetInstanceID())) Lighten(ps);
                if (!ps.isPlaying || !ps.main.loop || !OutOfSight(ps.transform.position, you, far)) continue;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                parked.Add(ps);
            }
        }

        /// <summary>A slice a frame, not a look every half second: walking through a door shouldn't leave the next room still for a moment.</summary>
        private static void WakeParked(Vector3 you, float far)
        {
            for (int n = Mathf.Min(WakeSlice, parked.Count); n > 0; n--)
            {
                if (--wakeAt < 0) wakeAt = parked.Count - 1;
                ParticleSystem ps = parked[wakeAt];
                if (ps != null && OutOfSight(ps.transform.position, you, far)) continue;
                if (ps != null) ps.Play(false);
                parked[wakeAt] = parked[parked.Count - 1];
                parked.RemoveAt(parked.Count - 1);
            }
        }

        internal static void Forget()
        {
            queue.Clear();
            foreach (Stack<Actions.VfxLife> stack in pools.Values)
                foreach (Actions.VfxLife life in stack)
                    if (life != null) Object.Destroy(life.gameObject); // they're switched off, so nothing else would ever clear them
            pools.Clear();
            seen.Clear();
            sweep = null;
            StatusLook.Forget();
            NodeIndex.Forget();
            ParticleGate.Forget();
            foreach (ParticleSystem ps in parked) if (ps != null) ps.Play(false);
            parked.Clear();
            wakeAt = 0;
        }

        /// <summary>A finished copy of this effect, ready to be placed, if one is kept.</summary>
        internal static Actions.VfxLife Reuse(string spec)
        {
            if (!EventBus.On || !pools.TryGetValue(spec, out var stack)) return null;
            while (stack.Count > 0)
            {
                Actions.VfxLife life = stack.Pop();
                if (life == null) continue; // went with its map
                Reused++;
                return life;
            }
            return null;
        }

        /// <summary>True when the effect was kept for next time, so it shouldn't be destroyed.</summary>
        internal static bool Keep(Actions.VfxLife life)
        {
            if (!EventBus.On || !life.reusable) return false;
            if (!pools.TryGetValue(life.spec, out var stack)) pools[life.spec] = stack = new Stack<Actions.VfxLife>();
            if (stack.Count >= PoolEach) return false;
            stack.Push(life);
            life.gameObject.SetActive(false);
            return true;
        }

        internal static void Lighten(GameObject go)
        {
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (seen.Add(ps.GetInstanceID())) Lighten(ps);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
        }

        private static void Lighten(ParticleSystem ps)
        {
            if (ps == null) return;
            Lightened++;
            var main = ps.main;
            if (main.cullingMode == ParticleSystemCullingMode.AlwaysSimulate) main.cullingMode = ParticleSystemCullingMode.Automatic; // off screen, it costs nothing
            var hits = ps.collision;
            if (hits.enabled && !hits.sendCollisionMessages) hits.enabled = false;
            var lights = ps.lights;
            if (lights.enabled) lights.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
        }
    }

    /// <summary>
    /// The combat log rebuilds all of its text for every line, so an area hit on eighty enemies rebuilds it eighty times in
    /// a frame. With the bus on, lines still arrive in order but the text is built once, on the way out of the frame.
    /// </summary>
    internal static class LogBatch
    {
        private static readonly AccessTools.FieldRef<Menu_HUD, List<string>> Lines = AccessTools.FieldRefAccess<Menu_HUD, List<string>>("logStrings");
        private static readonly AccessTools.FieldRef<Menu_HUD, TextMeshProUGUI> Text = AccessTools.FieldRefAccess<Menu_HUD, TextMeshProUGUI>("log");
        private static readonly AccessTools.FieldRef<Menu_HUD, float> Dock = AccessTools.FieldRefAccess<Menu_HUD, float>("yDock");
        private static readonly StringBuilder text = new StringBuilder(4096);
        private const float SecondsBetween = 0.2f, CalmAfter = 1f;
        private const int LinesInAFight = 24;
        private static bool dirty, cut;
        private static float nextAt, lastBuilt;

        /// <summary>
        /// Setting the text remakes every line of the log's mesh and sizes it again, and a full log makes most of the garbage in a
        /// fight doing so. While lines keep coming only the last ones are built, unless you've scrolled up; the rest are back once it calms down.
        /// </summary>
        internal static void Flush()
        {
            float now = Time.unscaledTime;
            if (cut && now - lastBuilt >= CalmAfter) dirty = true;
            if (!dirty || now < nextAt) return;
            nextAt = now + SecondsBetween;
            dirty = false;
            Menu_HUD hud = Menu_HUD.instance;
            if (hud == null) return;
            TextMeshProUGUI log = Text(hud);
            if (log == null) return;
            bool reading = hud.scroller != null && hud.scroller.value > 0.01f;
            bool fight = now - lastBuilt < CalmAfter && !reading;
            lastBuilt = now;
            List<string> lines = Lines(hud);
            int first = fight ? Mathf.Max(0, lines.Count - LinesInAFight) : 0;
            cut = first > 0;
            text.Clear();
            for (int i = first; i < lines.Count; i++)
                if (lines[i] != null) text.Append('\n').Append(lines[i]);
            log.text = text.ToString();
            Vector3 at = log.transform.localPosition;
            log.transform.localPosition = new Vector3(at.x, Dock(hud), at.z);
            if (hud.enabled) hud.StartCoroutine("ScrollUpdate");
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.UpdateText))]
        private static class Patch_UpdateText
        {
            private static bool Prefix()
            {
                if (!EventBus.On) return true;
                dirty = true;
                return false;
            }
        }
    }

    /// <summary>
    /// XP stays exact and counts at once; only what shows it waits. A kill resets the bar's text, and a level gained mid-spell
    /// rebuilds the whole skills menu with half the victims still to land, so a crowd's worth comes down to once a frame.
    /// </summary>
    internal static class XpBatch
    {
        private static bool earning, menuStale, barStale, flushing;
        private static int xp, need;

        internal static void Flush()
        {
            flushing = true;
            try
            {
                if (barStale && Menu_HUD.instance != null) Menu_HUD.instance.UpdateXpBar(xp, need);
                if (menuStale && Menu_Skills.instance != null) Menu_Skills.instance.Refresh();
            }
            finally { flushing = barStale = menuStale = false; }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddXp))]
        private static class Patch_Earning
        {
            private static void Prefix() => earning = true;
            private static void Finalizer() => earning = false;
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.UpdateXpBar))]
        private static class Patch_Bar
        {
            private static bool Prefix(int currentXp, int neededXp)
            {
                if (!EventBus.On || flushing) return true;
                xp = currentXp;
                need = neededXp;
                barStale = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Menu_Skills), nameof(Menu_Skills.Refresh))]
        private static class Patch_Menu
        {
            private static bool Prefix()
            {
                if (!EventBus.On || !earning || flushing) return true;
                menuStale = true;
                return false;
            }
        }
    }
}
