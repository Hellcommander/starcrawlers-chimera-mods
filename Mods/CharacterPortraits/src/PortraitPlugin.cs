// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Breachwork;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CharacterPortraits
{
    /// <summary>
    /// Make Portrait on the character picture picker: writes who the character is (classes, skills, gear) to a job file and
    /// starts the portrait tool the Tool setting points at, or with GameRender has the game render the character in their real
    /// gear on the GPU, no AI model. Every outside tool takes the same job file, and the portrait_tool.json in
    /// its folder says how to run it and whether it needs the GPU. It runs on its own, so the game plays on; when it exits,
    /// the new picture goes on the character.
    /// </summary>
    public class PortraitPlugin : BreachworkPlugin
    {
        private const string Manifest = "portrait_tool.json", GameRender = "GameRender";
        /// <summary>Mip levels dropped: an eighth of each side, a sixty-fourth of the memory.</summary>
        private const int LowestDetail = 3;

        private ConfigEntry<string> tool, python, appearance;
        private ConfigEntry<bool> showEffects;
        private ConfigEntry<int> paintingFps;
        private ConfigEntry<bool> lowTextures;
        private int fpsWas, vsyncWas, textureLimitWas;

        private class Job
        {
            public Process process;
            public string output, log, who;
            public bool creation, gpu;
            public float started, minutes;
        }

        /// <summary>One tool's portrait_tool.json: its name, the command ({job} and {python} get filled in), how long it may take, whether it shares the GPU.</summary>
        private class Tool
        {
            public string folder, name;
            public List<string> run;
            public bool gpu;
            public float minutes;
        }

        private Job job;
        private static string iconFolder;
        private static int iconCount;

        /// <summary>The last thing the plugin told the player.</summary>
        public static string LastMessage { get; private set; }

        public override void OnLoad()
        {
            tool = Context.Bind("Tool", GameRender, @"Which tool makes portraits: GameRender for the game's own GPU render of your character in their real gear, a folder name in this mod's tools folder, or the full path of any folder with a portrait_tool.json. For the AI toolkit that's <toolkit>\Tools\StarCrawlersChimera.");
            python = Context.Bind("Python", "python", "The Python that runs a Python tool such as the AI toolkit.");
            appearance = Context.Bind("Appearance", "", "How your characters look, in your own words: 'a scarred woman with short grey hair'. Empty leaves it to the skills and gear.");
            showEffects = Context.Bind("IncludeEffects", true, "Let what's on your character right now, such as a transformation, mutations and other buffs, show in the portrait.");
            paintingFps = Context.Bind("PaintingFrameRate", 20, "Caps the game's frame rate while a GPU tool paints, so the two can share the GPU. 0 leaves it alone.");
            lowTextures = Context.Bind("LowTexturesWhilePainting", true, "Drops the game's textures to their lowest detail while a GPU tool paints, freeing GPU memory. They come back when it's done.");
            Context.AddPortraitButton("Make Portrait", Start);
        }

        /// <summary>The tool the setting points at, or null after telling the player what's wrong.</summary>
        private Tool FindTool()
        {
            string folder = Path.IsPathRooted(tool.Value) ? tool.Value : Context.PathTo(Path.Combine("tools", tool.Value));
            string file = Path.Combine(folder, Manifest);
            if (!File.Exists(file))
            {
                Say($"No portrait tool at {folder}. Get the optional Portrait Maker download, or change Tool in Mod Settings.");
                return null;
            }
            try
            {
                JObject m = JObject.Parse(File.ReadAllText(file));
                var run = m["run"]?.ToObject<List<string>>();
                if (run == null || run.Count == 0) throw new InvalidDataException("it has no \"run\" command");
                return new Tool { folder = folder, name = (string)m["name"] ?? tool.Value, run = run, gpu = (bool?)m["gpu"] ?? false, minutes = (float?)m["minutes"] ?? 10f };
            }
            catch (Exception e)
            {
                Say($"Can't read {file}: {e.Message}");
                return null;
            }
        }

        /// <summary>The command's first word is a file in the tool's folder if there is one, or a program on the PATH.</summary>
        private ProcessStartInfo Command(Tool t, string jobFile)
        {
            List<string> words = t.run.Select(w => w.Replace("{job}", jobFile).Replace("{python}", python.Value)).ToList();
            string local = Path.Combine(t.folder, words[0]);
            return new ProcessStartInfo(File.Exists(local) ? local : words[0], string.Join(" ", words.Skip(1).Select(w => "\"" + w + "\"")))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = t.folder,
            };
        }

        /// <summary>The buffs and debuffs you can see on your character; hidden ones (synergies) aren't part of how they look.</summary>
        private static JArray Effects()
        {
            Effect form = Api.CurrentForm;
            var seen = Player.instance.effects.GetEffects().Where(e => e != null && e != form && !e.isHidden && !string.IsNullOrWhiteSpace(e.GetName()));
            return new JArray(seen.Select(e => new JObject { ["name"] = e.GetName(), ["debuff"] = e.isDebuff }));
        }

        private void Say(string text)
        {
            LastMessage = text;
            Context.Log.Info(text);
            if (Player.instance != null) Game.LOG("<color=#9FD3FF>" + text + "</color>");
        }

        private void Start(bool creation)
        {
            if (job != null) { Say($"Still making {job.who}'s portrait."); return; }
            if (string.Equals(tool.Value, GameRender, StringComparison.OrdinalIgnoreCase)) { RenderHere(); return; }
            Tool t = FindTool();
            if (t == null) return;

            string folder = Api.PortraitFolder, jobs = Path.Combine(folder, "jobs");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            iconFolder = Path.Combine(jobs, stamp + "_icons");
            iconCount = 0;
            Directory.CreateDirectory(iconFolder);
            JObject details = creation ? FromCreation() : FromRun();
            if (details == null) { Say("Pick your skills first: they decide what the portrait shows."); return; }
            string who = (string)details["name"];
            string slug = new string(who.Where(char.IsLetterOrDigit).ToArray());
            string file = (slug.Length > 0 ? slug : "Character") + "_" + stamp + ".png";
            string path = Path.Combine(jobs, stamp + ".json");
            details["appearance"] = appearance.Value;
            if (!creation && showEffects.Value)
            {
                details["effects"] = Effects();
                if (Api.CurrentForm != null) details["form"] = new JObject { ["name"] = Api.CurrentForm.GetName(), ["description"] = Api.CurrentForm.GetDescription() };
            }
            details["output"] = Path.Combine(folder, file);
            File.WriteAllText(path, details.ToString());

            ProcessStartInfo start = Command(t, path);
            start.EnvironmentVariables.Remove("NoDefaultCurrentDirectoryInExePath"); // breaks the toolkit's server launcher
            try { job = new Job { process = Process.Start(start), output = file, log = Path.ChangeExtension(path, ".log"), who = who, creation = creation, gpu = t.gpu, started = Time.realtimeSinceStartup, minutes = t.minutes }; }
            catch (Exception e) { Say($"Can't start {t.name} ({start.FileName}): {e.Message}"); return; }
            if (!t.gpu) return;
            textureLimitWas = QualitySettings.masterTextureLimit;
            if (lowTextures.Value) QualitySettings.masterTextureLimit = LowestDetail;
            if (paintingFps.Value > 0)
            {
                fpsWas = Application.targetFrameRate;
                vsyncWas = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = paintingFps.Value;
            }
            Say($"{t.name} is painting {who}. Play on: the portrait appears when it's done.");
        }

        private void RenderHere()
        {
            string file = new string((Player.instance?.playerName ?? "").Where(char.IsLetterOrDigit).ToArray()) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string why = Api.RenderPortrait(file);
            if (why != null) { Say(why); return; }
            Api.SetPortrait(file);
            Say("Your portrait is ready.");
        }

        public override void OnUpdate()
        {
            if (job == null || Time.frameCount % 30 != 0) return;
            if (!job.process.HasExited)
            {
                if (Time.realtimeSinceStartup - job.started < job.minutes * 60f) return;
                Say($"The portrait of {job.who} is taking too long. If it finishes, it will be in the Portraits folder.");
                RestoreFps(job);
                job = null;
                return;
            }
            Job done = job;
            job = null;
            RestoreFps(done);
            if (done.process.ExitCode != 0 || !File.Exists(Path.Combine(Api.PortraitFolder, done.output)))
            {
                Say($"The portrait of {done.who} failed: {Reason(done.log)}");
                return;
            }
            // Started on the creation screen but the game has begun since: it goes on the new character.
            Api.SetPortrait(done.output, done.creation && Player.instance == null);
            Say($"The portrait of {done.who} is ready.");
        }

        private void RestoreFps(Job j)
        {
            if (!j.gpu) return;
            QualitySettings.masterTextureLimit = textureLimitWas;
            if (paintingFps.Value <= 0) return;
            Application.targetFrameRate = fpsWas;
            QualitySettings.vSyncCount = vsyncWas;
        }

        private static string Reason(string log)
        {
            try { return File.ReadLines(log).LastOrDefault(l => l.Trim().Length > 0) ?? "no reason given"; }
            catch (Exception) { return "see " + log; }
        }

        // ---- who the character is

        private static string Slot(Item_Equipment item) => item.equipType.ToString();

        private static JObject Gear(string slot, Item item)
        {
            var gear = new JObject { ["slot"] = slot, ["name"] = item.GetName(false), ["description"] = item.GetDescription() ?? "" };
            try
            {
                Texture2D icon = Readable(item.GetInventorySprite());
                if (icon == null) return gear;
                gear["colors"] = new JArray(MainColors(icon).Select(c => "#" + ColorUtility.ToHtmlStringRGB(c)));
                string path = Path.Combine(iconFolder, iconCount++ + ".png");
                File.WriteAllBytes(path, icon.EncodeToPNG());
                gear["icon"] = path;
                UnityEngine.Object.Destroy(icon);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("Portrait icon of " + item.GetName(false) + ": " + e.Message); }
            return gear;
        }

        /// <summary>A copy of the sprite's own part of its texture, which is usually an atlas the game can't read back.</summary>
        private static Texture2D Readable(Sprite sprite)
        {
            if (sprite == null) return null;
            Rect r = sprite.textureRect;
            Texture t = sprite.texture;
            int w = (int)r.width, h = (int)r.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(t, rt, new Vector2(r.width / t.width, r.height / t.height), new Vector2(r.x / t.width, r.y / t.height));
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        /// <summary>The icon's commonest colours, commonest first: coarse buckets over the solid pixels.</summary>
        private static List<Color> MainColors(Texture2D icon)
        {
            var buckets = new Dictionary<int, (Color sum, int n)>();
            foreach (Color32 p in icon.GetPixels32())
            {
                if (p.a < 128 || p.r + p.g + p.b < 60) continue;
                int key = (p.r >> 5) << 6 | (p.g >> 5) << 3 | p.b >> 5;
                buckets.TryGetValue(key, out var b);
                buckets[key] = (b.sum + (Color)p, b.n + 1);
            }
            int solid = buckets.Values.Sum(b => b.n);
            return buckets.Values.Where(b => b.n * 25 >= solid).OrderByDescending(b => b.n).Take(3)
                .Select(b => { Color c = b.sum / b.n; c.a = 1f; return c; }).ToList();
        }

        private static JObject Tree(Skill_Tree t) => new JObject
        {
            ["name"] = (string)t.displayName,
            ["rank"] = t.GetRank() + 1,
            ["description"] = (string)t.description,
        };

        private static JObject FromRun()
        {
            Player p = Player.instance;
            if (p == null || Player_SkillControl.instance == null) return null;
            var gear = new List<JObject>();
            if (p.mainHandWeapon != null) gear.Add(Gear("main hand", p.mainHandWeapon));
            if (p.offhandWeapon != null && p.offhandWeapon != p.mainHandWeapon) gear.Add(Gear("off hand", p.offhandWeapon));
            if (Menu_Player.instance != null)
                foreach (Item_Equipment e in Menu_Player.instance.GetEquipment())
                    if (e != null && e != p.mainHandWeapon && e != p.offhandWeapon) gear.Add(Gear(Slot(e), e));
            // the other weapon set, which the game keeps to itself until you swap
            foreach (string field in new[] { "weaponSet_mainhand", "weaponSet_offhand" })
                if (Menu_Player.instance != null && typeof(Menu_Player).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Menu_Player.instance) is Item_Weapon stowed)
                    gear.Add(Gear("stowed", stowed));
            var abilities = Player_AbilityControl.instance?.GetAbilities().Where(a => a != null).Select(a => a.GetDisplayName()).Distinct().ToList() ?? new List<string>();
            return new JObject
            {
                ["name"] = p.playerName,
                ["level"] = p.level,
                ["classes"] = new JArray(Player_SkillControl.instance.skillTrees.Where(t => t != null).OrderByDescending(t => t.GetRank()).Select(Tree)),
                ["abilities"] = new JArray(abilities),
                ["gear"] = new JArray(gear),
            };
        }

        private static JObject FromCreation()
        {
            Menu_Creation mc = Menu_Creation.instance;
            var trees = mc == null ? new List<Skill_Tree>() : mc.selectedSkills.Where(s => s != null && s.skillTree != null).Select(s => s.skillTree).ToList();
            if (trees.Count == 0) return null;
            string name = mc.nameInput != null ? mc.nameInput.text : "";
            return new JObject
            {
                ["name"] = string.IsNullOrWhiteSpace(name) ? "Character" : name.Trim(),
                ["level"] = 1,
                ["classes"] = new JArray(trees.Select(Tree)),
                ["abilities"] = new JArray(),
                ["gear"] = new JArray(trees.SelectMany(t => t.starterGear_items ?? new Item[0]).Where(i => i != null).Distinct().Select(i => Gear(i is Item_Equipment e ? Slot(e) : "carried", i))),
            };
        }
    }
}
