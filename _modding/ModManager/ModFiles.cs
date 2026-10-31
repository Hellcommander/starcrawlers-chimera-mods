using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCClassLoader;

namespace ChimeraModManager
{
    /// <summary>
    /// The game folder as the manager sees it: the mods (read by the loader's own ModCore), the last game start's log,
    /// which saves use which classes, and installing, removing and launching.
    /// </summary>
    internal class ModFiles
    {
        internal readonly string GameDir, ModsDir, LogFile, SavesDir;
        internal List<(string file, string error)> ScanErrors = new List<(string, string)>();
        /// <summary>Errors and warnings the loader wrote on the last game start.</summary>
        internal List<(bool error, string text)> Log = new List<(bool, string)>();
        internal DateTime? LogTime;
        /// <summary>Class id -> names of the saves that use it.</summary>
        internal Dictionary<string, List<string>> SaveUse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        internal ModFiles(string gameDir)
        {
            GameDir = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
            ModsDir = Path.Combine(GameDir, "Mods");
            LogFile = Path.Combine(GameDir, "BepInEx", "LogOutput.log");
            // Unity's persistentDataPath: %USERPROFILE%\AppData\LocalLow\<company>\<product>
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            SavesDir = Path.Combine(Path.GetDirectoryName(local), "LocalLow", "Juggernaut Games", "StarCrawlers Chimera", "Saves");
            ModChoices.ConfigDir = Path.Combine(GameDir, "BepInEx", "config");
        }

        internal bool HasLoader => File.Exists(Path.Combine(GameDir, "BepInEx", "plugins", "StarCrawlersClassLoader", "StarCrawlersClassLoader.dll"));

        internal void Rescan()
        {
            Directory.CreateDirectory(ModsDir);
            ScanErrors = ModPacks.Scan(ModsDir, ReadXml);
            ReadLog();
            ReadSaves();
        }

        internal bool IsRoot(ModPack p) => string.Equals(p.folder.TrimEnd(Path.DirectorySeparatorChar), ModsDir, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// XML mod files, read just far enough to tell classes, libraries and data apart (the loader's own XML reader
        /// knows the whole format and needs the game).
        /// </summary>
        private static JToken ReadXml(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            if (doc.DocumentElement == null) throw new InvalidDataException("empty XML file");
            JToken tok = JToken.Parse(JsonConvert.SerializeXmlNode(doc.DocumentElement, Newtonsoft.Json.Formatting.None, true));
            if (tok is JObject o && o["tiers"] == null && o["class"] != null)
                return o["class"] is JArray a ? a : new JArray(o["class"]);
            return tok is JObject ? tok : new JObject();
        }

        private static readonly Regex LogLine = new Regex(@"^\[(Error|Warning|Fatal)\s*:\s*Breachwork Loader\]\s?(.*)$");

        private void ReadLog()
        {
            Log = new List<(bool, string)>();
            LogTime = null;
            if (!File.Exists(LogFile)) return;
            LogTime = File.GetLastWriteTime(LogFile);
            // The game keeps the log open while it runs.
            using (var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
                for (string line; (line = r.ReadLine()) != null;)
                {
                    Match m = LogLine.Match(line);
                    if (m.Success) Log.Add((m.Groups[1].Value != "Warning", m.Groups[2].Value));
                }
        }

        /// <summary>
        /// Saves name their skill trees as plain strings, so a class is in use when its id is one of the save's strings.
        /// (Reading saves properly takes the game's own code.)
        /// </summary>
        private void ReadSaves()
        {
            SaveUse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(SavesDir)) return;
            var ids = new HashSet<string>(ModPacks.All.SelectMany(p => p.classes.Select(c => c.id)), StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(SavesDir, "*.dat"))
            {
                try
                {
                    foreach (string s in Strings(File.ReadAllBytes(f)).Where(ids.Contains))
                    {
                        if (!SaveUse.TryGetValue(s, out var l)) SaveUse[s] = l = new List<string>();
                        l.Add(Path.GetFileNameWithoutExtension(f));
                    }
                }
                catch (IOException) { } // in use or gone: nothing to list for it
            }
        }

        /// <summary>Length-prefixed identifier strings (as the save serializer writes them).</summary>
        private static HashSet<string> Strings(byte[] b)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < b.Length - 4; i++)
            {
                int n = b[i];
                if (n < 3 || n > 100 || i + n >= b.Length) continue;
                bool ok = true;
                for (int k = 1; k <= n && ok; k++)
                {
                    byte c = b[i + k];
                    ok = c == '_' || c >= '0' && c <= '9' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
                }
                if (ok) found.Add(Encoding.ASCII.GetString(b, i + 1, n));
            }
            return found;
        }

        internal bool GameRunning
        {
            get
            {
                Process[] ps = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Program.GameExe));
                foreach (Process p in ps) p.Dispose();
                return ps.Length > 0;
            }
        }

        internal void Play() => Process.Start(new ProcessStartInfo(Path.Combine(GameDir, Program.GameExe)) { WorkingDirectory = GameDir, UseShellExecute = true });

        internal static void OpenFolder(string path) => Process.Start("explorer.exe", $"\"{path}\"");

        // ---- installing and removing

        /// <summary>
        /// Installs the mods in a .zip or a folder into Mods/ and returns their folder names. A mod already installed
        /// (same id, or same folder name) is replaced, once <paramref name="replace"/> agrees; the old copy goes to the
        /// Recycle Bin.
        /// </summary>
        internal List<string> Install(string source, Func<string, bool> replace)
        {
            string temp = null;
            try
            {
                string dir = source.TrimEnd(Path.DirectorySeparatorChar), hint = Path.GetFileNameWithoutExtension(dir);
                if (File.Exists(source))
                {
                    if (!source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Only .zip archives and folders can be installed. Unpack other archives (.7z, .rar) first and install the folder.");
                    temp = Path.Combine(Path.GetTempPath(), "BreachworkModManager_" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(source, temp);
                    dir = temp;
                }
                else if (!Directory.Exists(source)) throw new FileNotFoundException("Not found: " + source);
                else if (Under(Path.GetFullPath(dir), ModsDir)) throw new InvalidDataException("That folder is already in the Mods folder.");

                var roots = ModRoots(dir, hint);
                if (roots.Count == 0) throw new InvalidDataException("No mod found in it. A mod is a folder with a mod.json, or a folder of .json/.xml mod files.");
                var done = new List<string>();
                foreach (var (folder, name) in roots)
                {
                    string id = ManifestId(folder) ?? ModPacks.Sanitize(name);
                    ModPack old = ModPacks.Find(id);
                    string target = old != null && !IsRoot(old) ? old.folder : Path.Combine(ModsDir, name);
                    if (Directory.Exists(target))
                    {
                        if (!replace(target)) continue;
                        Recycle(target);
                    }
                    CopyDir(folder, target);
                    done.Add(Path.GetFileName(target));
                }
                return done;
            }
            finally { if (temp != null && Directory.Exists(temp)) Directory.Delete(temp, true); }
        }

        /// <summary>Where the mods are in an unpacked download: a release's Mods folder, folders with a mod.json, or one folder of mod files.</summary>
        private static List<(string folder, string name)> ModRoots(string dir, string hint)
        {
            string mods = Path.Combine(dir, "Mods");
            if (Directory.Exists(mods))
                return Directory.GetDirectories(mods).Where(HasModFiles).Select(d => (d, Path.GetFileName(d))).ToList();
            var manifests = Directory.GetFiles(dir, "mod.json", System.IO.SearchOption.AllDirectories).Select(Path.GetDirectoryName).ToList();
            var tops = manifests.Where(m => !manifests.Any(o => o != m && Under(m, o))).ToList();
            if (tops.Count > 0) return tops.Select(t => (t, t == dir ? hint : Path.GetFileName(t))).ToList();
            string[] subs = Directory.GetDirectories(dir);
            if (Directory.GetFiles(dir).Length == 0 && subs.Length == 1) return HasModFiles(subs[0]) ? new List<(string, string)> { (subs[0], Path.GetFileName(subs[0])) } : new List<(string, string)>();
            return HasModFiles(dir) ? new List<(string, string)> { (dir, hint) } : new List<(string, string)>();
        }

        private static bool HasModFiles(string dir) =>
            Directory.EnumerateFiles(dir, "*.*", System.IO.SearchOption.AllDirectories).Any(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        private static string ManifestId(string folder)
        {
            string mf = Path.Combine(folder, "mod.json");
            if (!File.Exists(mf)) return null;
            try { string id = (string)JObject.Parse(File.ReadAllText(mf))["id"]; return string.IsNullOrEmpty(id) ? null : ModPacks.Sanitize(id); }
            catch (JsonException) { return null; }
        }

        private static bool Under(string path, string dir) =>
            path.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        /// <summary>Moves a mod's folder to the Recycle Bin.</summary>
        internal void Remove(ModPack p)
        {
            if (IsRoot(p) || !Under(p.folder, ModsDir)) throw new InvalidOperationException("Only a mod's own folder inside Mods can be removed.");
            Recycle(p.folder);
        }

        private static void Recycle(string dir) => FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
