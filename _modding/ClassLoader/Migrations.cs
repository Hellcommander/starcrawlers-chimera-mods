using System;
using System.IO;

namespace SCClassLoader
{
    /// <summary>
    /// Moves files an older install left under a name we've since changed. Runs once per start, before anything else
    /// reads them. So far: the mod manager, ChimeraModManager.exe, became BreachworkModManager.exe in the Breachwork
    /// rename. An update that brought the new exe gets the old one removed; an install with only the old one gets it
    /// renamed, so shortcuts and the docs point at a file that exists.
    /// </summary>
    internal static class Migrations
    {
        private static readonly (string from, string to)[] Renamed =
        {
            ("ChimeraModManager.exe", "BreachworkModManager.exe"),
        };

        internal static void Run(string gameDir)
        {
            foreach (var (from, to) in Renamed)
            {
                string oldPath = Path.Combine(gameDir, from), newPath = Path.Combine(gameDir, to);
                if (!File.Exists(oldPath)) continue;
                try
                {
                    if (File.Exists(newPath))
                    {
                        File.Delete(oldPath);
                        Plugin.Log.LogInfo($"Removed {from}: {to} replaces it.");
                    }
                    else
                    {
                        File.Move(oldPath, newPath);
                        Plugin.Log.LogInfo($"Renamed {from} to {to}.");
                    }
                }
                // Open in another program, or a read-only folder: leave it for the next start.
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't move {from} to {to}: {e.Message}"); }
            }
        }
    }
}
