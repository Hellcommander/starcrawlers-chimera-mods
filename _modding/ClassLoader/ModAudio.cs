using System;
using System.Collections.Generic;
using System.IO;
using FMODUnity;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Plays a "sound" value: vanilla FMOD events ("event:/...") go through the game's AudioManager; anything else is a
    /// .wav/.ogg/.mp3 file next to the class file (or in the library folder), played through FMOD core on the game's
    /// Sound bus so the in-game volume sliders apply. A file that is missing or won't load plays a matching vanilla event
    /// instead ("soundFallbacks" in a mod's json, by file name): the audio isn't in the source repository, so a build from a
    /// plain clone has none of these files. A loose file wins, then a mod's sound archive (ModArchives), then the fallback.
    /// </summary>
    internal static class ModAudio
    {
        private static readonly Dictionary<string, FMOD.Sound> cache = new Dictionary<string, FMOD.Sound>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase), announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Class -> sound value -> where it is (full path and "file" or "archive", or a null path for a missing sound), settled
        /// the first time a sound is asked for, like the FMOD sound itself: looking it up again every play (four path
        /// checks on disk) cost half a millisecond and a kilobyte of garbage per sound. Forget() clears it too.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, (string path, string source)>> resolved = new Dictionary<string, Dictionary<string, (string, string)>>();
        private static FMOD.ChannelGroup soundBus;
        private static bool busReady;
        /// <summary>File name without extension -> vanilla event, from every loaded mod's "soundFallbacks".</summary>
        private static Dictionary<string, string> fallbacks;
        private static string fallbackDefault;
        /// <summary>Where the last sound came from, and its FMOD result (self-tests).</summary>
        internal static string LastSource;
        internal static string LastResult => lastResult.ToString();
        private static FMOD.RESULT lastResult;

        internal static void Forget() { cache.Clear(); resolved.Clear(); }

        /// <summary>Self-test: plays archives even where a loose file exists.</summary>
        internal static bool ArchiveFirst;

        private static bool warmed;

        /// <summary>
        /// One silent play once everything is prepared, so the first real sound doesn't pay for finding the Sound bus and
        /// compiling this path: that first call alone took tens of milliseconds.
        /// </summary>
        private static void Warm()
        {
            if (warmed) return;
            warmed = true;
            foreach (string path in cache.Keys)
            {
                Play(path, Vector3.zero, ClassRegistry.LibId, 0f);
                break;
            }
        }

        /// <summary>Sounds FMOD is preparing on its own thread; each keeps its bytes until it's done.</summary>
        private static readonly Dictionary<string, (FMOD.Sound sound, byte[] data)> preparing = new Dictionary<string, (FMOD.Sound, byte[])>(StringComparer.OrdinalIgnoreCase);
        private const int PreparePerFrame = 8;

        internal static int Preparing => preparing.Count + ModArchives.Ready.Count;

        /// <summary>Hands the preloaded archive sounds to FMOD, a few a frame, and keeps the ones it has finished.</summary>
        internal static void Tick()
        {
            if (preparing.Count == 0 && ModArchives.Ready.IsEmpty)
            {
                if (ModArchives.PreloadDone) Warm();
                return;
            }
            try
            {
                FMOD.System core = RuntimeManager.CoreSystem;
                for (int i = 0; i < PreparePerFrame && ModArchives.Ready.TryDequeue(out var next); i++)
                {
                    if (cache.ContainsKey(next.path)) continue;
                    var info = new FMOD.CREATESOUNDEXINFO { length = (uint)next.data.Length };
                    info.cbsize = System.Runtime.InteropServices.Marshal.SizeOf(info);
                    FMOD.RESULT r = core.createSound(next.data, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D | FMOD.MODE.NONBLOCKING, ref info, out FMOD.Sound snd);
                    if (r == FMOD.RESULT.OK) preparing[next.path] = (snd, next.data);
                    else Plugin.Log.LogWarning($"FMOD could not prepare {next.path}: {r}");
                }
                foreach (var kv in new List<KeyValuePair<string, (FMOD.Sound sound, byte[] data)>>(preparing))
                {
                    kv.Value.sound.getOpenState(out FMOD.OPENSTATE state, out uint _, out bool _, out bool _);
                    if (state == FMOD.OPENSTATE.READY)
                    {
                        preparing.Remove(kv.Key);
                        if (cache.ContainsKey(kv.Key)) kv.Value.sound.release(); // played before it was ready: made the slow way
                        else cache[kv.Key] = kv.Value.sound;
                    }
                    else if (state == FMOD.OPENSTATE.ERROR)
                    {
                        preparing.Remove(kv.Key);
                        kv.Value.sound.release();
                        Plugin.Log.LogWarning($"FMOD could not prepare {kv.Key}");
                    }
                }
            }
            catch (Exception e)
            {
                if (warned.Add("tick")) Plugin.Log.LogWarning($"Preparing sounds failed: {e.Message}");
            }
        }

        private static void LoadFallbacks()
        {
            fallbacks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    if (!(obj["soundFallbacks"] is Newtonsoft.Json.Linq.JObject f)) continue;
                    fallbackDefault = (string)f["default"] ?? fallbackDefault;
                    foreach (Newtonsoft.Json.Linq.JProperty s in (f["sounds"] as Newtonsoft.Json.Linq.JObject)?.Properties() ?? System.Linq.Enumerable.Empty<Newtonsoft.Json.Linq.JProperty>())
                        fallbacks[s.Name] = (string)s.Value;
                }
        }

        /// <summary>The vanilla event a missing sound file plays; null when no mod names one.</summary>
        internal static string FallbackFor(string spec)
        {
            if (fallbacks == null) LoadFallbacks();
            return fallbacks.TryGetValue(Path.GetFileNameWithoutExtension(spec), out string e) ? e : fallbackDefault;
        }

        private static void PlayFallback(string spec, Vector3 pos)
        {
            string e = FallbackFor(spec);
            LastSource = "fallback";
            if (e != null) AudioManager.PlayEvent(e, pos);
        }

        public static void Play(string spec, Vector3 pos, string classId) => Play(spec, pos, classId, 1f);

        /// <summary>volume 0..1 scales mod files only; vanilla events play at their own level.</summary>
        public static void Play(string spec, Vector3 pos, string classId, float volume)
        {
            if (string.IsNullOrEmpty(spec)) return;
            try
            {
                if (spec.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) { AudioManager.PlayEvent(spec, pos); return; }
                if (!resolved.TryGetValue(classId ?? "", out var perClass)) resolved[classId ?? ""] = perClass = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                if (!perClass.TryGetValue(spec, out var where))
                {
                    string loose = ArchiveFirst ? null : Resolve(spec, classId);
                    where = loose != null ? (loose, "file") : (InArchive(spec, classId), "archive");
                    perClass[spec] = where;
                }
                string path = where.path;
                if (path == null)
                {
                    if (warned.Add(spec)) Plugin.Log.LogInfo($"Sound file not found, using the game's: {spec} ({classId}) -> {FallbackFor(spec) ?? "nothing"}");
                    PlayFallback(spec, pos);
                    return;
                }
                FMOD.System core = RuntimeManager.CoreSystem;
                if (!cache.TryGetValue(path, out FMOD.Sound snd))
                {
                    // Asked for before the preload reached it (or a loose file): made here and now, on this thread.
                    FMOD.RESULT r;
                    if (where.source == "archive")
                    {
                        byte[] packed = ModArchives.Read(path);
                        if (packed == null) { PlayFallback(spec, pos); return; }
                        var info = new FMOD.CREATESOUNDEXINFO { length = (uint)packed.Length };
                        info.cbsize = System.Runtime.InteropServices.Marshal.SizeOf(info);
                        r = core.createSound(packed, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D, ref info, out snd);
                    }
                    else r = core.createSound(path, FMOD.MODE.DEFAULT | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D, out snd);
                    if (r != FMOD.RESULT.OK) { if (warned.Add(path)) Plugin.Log.LogWarning($"FMOD could not load {path}: {r}"); PlayFallback(spec, pos); return; }
                    cache[path] = snd;
                }
                FMOD.RESULT pr = core.playSound(snd, SoundBus(), volume < 0.999f, out FMOD.Channel ch);
                LastSource = where.source;
                lastResult = pr;
                if (volume < 0.999f) { ch.setVolume(Mathf.Clamp01(volume)); ch.setPaused(false); }
                if (announced.Add(path)) Plugin.Log.LogInfo($"Custom sound {Path.GetFileName(path)}: {pr}");
            }
            catch (Exception e)
            {
                if (warned.Add("ex:" + spec)) Plugin.Log.LogWarning($"Sound '{spec}' failed: {e.Message}");
            }
        }

        private static string Resolve(string spec, string classId)
        {
            if (Path.IsPathRooted(spec)) return File.Exists(spec) ? spec : null;
            foreach (string dir in new[] { ClassRegistry.FolderOf(classId), ClassRegistry.LibraryDef?.folder })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.GetFullPath(Path.Combine(dir, spec));
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>The sound's own path if an archive holds it.</summary>
        private static string InArchive(string spec, string classId)
        {
            if (Path.IsPathRooted(spec)) return ModArchives.Has(spec) ? spec : null;
            foreach (string dir in new[] { ClassRegistry.FolderOf(classId), ClassRegistry.LibraryDef?.folder })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.GetFullPath(Path.Combine(dir, spec));
                if (ModArchives.Has(p)) return p;
            }
            return null;
        }

        /// <summary>The game's "Bus:/Sound" channel group, so the Sound volume slider applies. Falls back to master.</summary>
        private static FMOD.ChannelGroup SoundBus()
        {
            if (busReady) return soundBus;
            busReady = true;
            try
            {
                FMOD.Studio.Bus bus = RuntimeManager.GetBus("Bus:/Sound");
                bus.lockChannelGroup();
                RuntimeManager.StudioSystem.flushCommands();
                if (bus.getChannelGroup(out soundBus) != FMOD.RESULT.OK) RuntimeManager.CoreSystem.getMasterChannelGroup(out soundBus);
            }
            catch { RuntimeManager.CoreSystem.getMasterChannelGroup(out soundBus); }
            return soundBus;
        }
    }
}
