using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SCClassLoader
{
    /// <summary>The loader's side of <see cref="ModPacks"/> (the rest is in ModCore.cs): the plan this session loaded with, and its log lines.</summary>
    internal static partial class ModPacks
    {
        internal static List<ModPack> Ordered = new List<ModPack>();
        /// <summary>The plan this session loaded with.</summary>
        internal static ModPlan Plan = new ModPlan();

        /// <summary>Orders this session's enabled mods and logs the ones left out.</summary>
        internal static void Order()
        {
            if (ModChoices.LoadError != null) Plugin.Log.LogError($"Could not read {ModChoices.FilePath}: {ModChoices.LoadError}. Every mod uses its own mod.json setting.");
            Plan = Resolve(p => p.enabled);
            Ordered = Plan.Ordered;
            foreach (var kv in Plan.Skipped.OrderBy(kv => kv.Key.id))
            {
                if (kv.Value == Off) Plugin.Log.LogInfo($"Mod '{kv.Key.id}' is turned off.");
                else if (kv.Key.manifestError != null) Plugin.Log.LogError($"Could not read {Path.Combine(kv.Key.folder, "mod.json")}: {kv.Key.manifestError}. That mod is not loaded.");
                else Plugin.Log.LogError($"Mod '{kv.Key.id}' is not loaded: {kv.Value}.");
            }
            foreach (string w in Plan.Warnings) Plugin.Log.LogWarning(w);
            if (ModChoices.Order.Count > 0) Plugin.Log.LogInfo("Mod load order (chosen): " + string.Join(", ", Ordered.Select(p => p.id)));

            foreach (ModPack p in Ordered.Where(p => p.hasManifest))
                Plugin.Log.LogInfo($"Mod '{p.name ?? p.id}' ({p.id}) {p.version} by {p.author ?? "?"}: {p.data.Count} data file(s)");
        }

        internal static bool IsLoaded(ModPack p) => Ordered.Contains(p);

        /// <summary>The kinds and entries that were off when this session loaded.</summary>
        internal static HashSet<string> StartOff = new HashSet<string>();

        /// <summary>
        /// Takes turned-off classes, items, synergies, tweaks, ... out of the mods' files before anything reads them, so a
        /// turned-off entry is simply not there (a turned-off patch leaves what it patches as it was).
        /// </summary>
        internal static void PruneOff()
        {
            StartOff = new HashSet<string>(ModChoices.OffParts, System.StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (ModPack p in All)
                foreach (ContentGroup g in p.content)
                    foreach (ContentEntry e in g.entries)
                    {
                        if (ModChoices.PartOn(g.key) && ModChoices.PartOn(e.key)) continue;
                        if (e.isClass) p.classObjs.RemoveAll(t => ReferenceEquals(t.obj, e.token));
                        else if (e.token.Parent != null) e.token.Remove();
                        n++;
                    }
            if (n > 0) Plugin.Log.LogInfo($"Mod content turned off: {n} entries ({string.Join(", ", StartOff.OrderBy(k => k))})");
        }

        /// <summary>Whether the choices differ from what this session loaded, so a restart would change something.</summary>
        internal static bool ChangedSinceStart() =>
            All.Any(p => ModChoices.IsOn(p) != p.enabled) || !StartOff.SetEquals(ModChoices.OffParts);
    }
}
