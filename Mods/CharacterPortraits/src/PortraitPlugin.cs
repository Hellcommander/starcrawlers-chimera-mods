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
    /// Paint Portrait on the character picture picker: writes who the character is (classes, skills, gear) to a job file and
    /// starts the AI toolkit's make_portrait.py on it. That runs on its own, so the game plays on while it paints; when it
    /// exits, the new picture goes on the character.
    /// </summary>
    public class PortraitPlugin : BreachworkPlugin
    {
        private const string Script = @"Tools\StarCrawlersChimera\make_portrait.py";
        private const float MaxMinutes = 10f;
        /// <summary>Mip levels dropped: an eighth of each side, a sixty-fourth of the memory.</summary>
        private const int LowestDetail = 3;

        private ConfigEntry<string> toolkit, python, appearance;
        private ConfigEntry<bool> showEffects;
        private ConfigEntry<int> paintingFps;
        private ConfigEntry<bool> lowTextures;
        private int fpsWas, vsyncWas, textureLimitWas;

        private class Job
        {
            public Process process;
            public string output, log, who;
            public bool creation;
            public float started;
        }

        private Job job;

        /// <summary>The last thing the plugin told the player.</summary>
        public static string LastMessage { get; private set; }

        public override void OnLoad()
        {
            toolkit = Context.Bind("ToolkitFolder", @"D:\games\Ai assisted toolkit", "Where the AI asset toolkit is.");
            python = Context.Bind("Python", "python", "The Python that runs the toolkit.");
            appearance = Context.Bind("Appearance", "", "How your characters look, in your own words: 'a scarred woman with short grey hair'. Empty leaves it to the skills and gear.");
            showEffects = Context.Bind("IncludeEffects", true, "Let what's on your character right now, such as a transformation, mutations and other buffs, show in the portrait.");
            paintingFps = Context.Bind("PaintingFrameRate", 20, "Caps the game's frame rate while a portrait paints, so the two can share the GPU. 0 leaves it alone.");
            lowTextures = Context.Bind("LowTexturesWhilePainting", true, "Drops the game's textures to their lowest detail while a portrait paints, freeing GPU memory. They come back when it's done.");
            Context.AddPortraitButton("Paint Portrait", Start);
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
            if (job != null) { Say($"Still painting {job.who}."); return; }
            string script = Path.Combine(toolkit.Value, Script);
            if (!File.Exists(script))
            {
                Say("Can't paint a portrait: the toolkit isn't at " + toolkit.Value + " (ToolkitFolder in Mod Settings).");
                return;
            }

            JObject details = creation ? FromCreation() : FromRun();
            if (details == null) { Say("Pick your skills first: they decide what the portrait shows."); return; }
            string who = (string)details["name"];
            string folder = Api.PortraitFolder, jobs = Path.Combine(folder, "jobs");
            Directory.CreateDirectory(jobs);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
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

            var start = new ProcessStartInfo(python.Value, $"\"{script}\" \"{path}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(script) };
            start.EnvironmentVariables.Remove("NoDefaultCurrentDirectoryInExePath"); // breaks the toolkit's server launcher
            try { job = new Job { process = Process.Start(start), output = file, log = Path.ChangeExtension(path, ".log"), who = who, creation = creation, started = Time.realtimeSinceStartup }; }
            catch (Exception e) { Say($"Can't start {python.Value}: {e.Message} (Python in Mod Settings)"); return; }
            textureLimitWas = QualitySettings.masterTextureLimit;
            if (lowTextures.Value) QualitySettings.masterTextureLimit = LowestDetail;
            if (paintingFps.Value > 0)
            {
                fpsWas = Application.targetFrameRate;
                vsyncWas = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = paintingFps.Value;
            }
            Say($"Painting a portrait of {who}. Play on: it appears when it's done.");
        }

        public override void OnUpdate()
        {
            if (job == null || Time.frameCount % 30 != 0) return;
            if (!job.process.HasExited)
            {
                if (Time.realtimeSinceStartup - job.started < MaxMinutes * 60f) return;
                Say($"The portrait of {job.who} is taking too long; the GPU may be too busy. If it finishes, it will be in the Portraits folder.");
                job = null;
                RestoreFps();
                return;
            }
            Job done = job;
            job = null;
            RestoreFps();
            if (done.process.ExitCode != 0 || !File.Exists(Path.Combine(Api.PortraitFolder, done.output)))
            {
                Say($"The portrait of {done.who} failed: {Reason(done.log)}");
                return;
            }
            // Started on the creation screen but the game has begun since: it goes on the new character.
            Api.SetPortrait(done.output, done.creation && Player.instance == null);
            Say($"The portrait of {done.who} is ready.");
        }

        private void RestoreFps()
        {
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

        private static JObject Gear(string slot, Item item) => new JObject { ["slot"] = slot, ["name"] = item.GetName(false) };

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
