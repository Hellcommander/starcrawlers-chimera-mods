using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The game's Feedback button (Esc menu) goes to Juggernaut Games, who can't fix bugs that come from mods. With mods
    /// loaded it asks first and points at the mod repo's issue page instead: the browser opens a new issue with the
    /// game, loader and mod versions, the character and this session's errors filled in, and the tail of the log is on
    /// the clipboard to paste below. The game's own form is still one button away.
    /// </summary>
    internal static class BugReport
    {
        internal const string IssuesUrl = "https://github.com/Hellcommander/starcrawlers-chimera-mods/issues";

        internal static ConfigEntry<bool> Ask;

        /// <summary>What opens the page (the self-test swaps it so no browser opens).</summary>
        internal static Action<string> Opener = Application.OpenURL;

        private const int MaxErrors = 12, LogLines = 150, MaxUrl = 7500;

        internal static void Bind(ConfigFile cfg)
        {
            Ask = cfg.Bind("BugReports", "AskBeforeGameFeedback", true,
                "With mods loaded, the Esc menu's Feedback button first offers the mods' issue page (base-game feedback can't fix mod bugs).");
        }

        private static bool passThrough;

        [HarmonyPatch(typeof(HUD), nameof(HUD.BTN_ShowFeedback))]
        private static class Patch_Feedback
        {
            private static bool Prefix(HUD __instance)
            {
                if (passThrough || !Ask.Value || ModPacks.Ordered.Count == 0) return true;
                if (Menu_System.instance != null && Menu_System.instance.GetIsShowing()) Menu_System.instance.Hide();
                Choice.Show("SCCL_BugReport", "<color=#FFB84D>Is this a mod bug?</color>",
                    $"You're playing with {ModPacks.Ordered.Count} mod{(ModPacks.Ordered.Count == 1 ? "" : "s")} loaded. The game's Feedback form goes to "
                    + "the StarCrawlers developers, who can't fix bugs that come from mods, so unless you're sure it also happens "
                    + "without them, please report it on the mods' GitHub page instead.\n\n"
                    + "<color=#9FB7C9>Report on GitHub opens a new issue in your browser with your versions, mods, character and this "
                    + "session's errors filled in, and copies the end of the log: paste it into the report. Posting needs a free "
                    + "GitHub account. If it only happens with someone else's mod, tell that mod's author.</color>",
                    new List<(string, Action, Color?)>
                    {
                        ("Report on GitHub", Open, UiKit.OnColor),
                        ("Game's form anyway", () => GameForm(__instance), null),
                        ("Cancel", null, null),
                    });
                return false;
            }
        }

        private static void GameForm(HUD hud)
        {
            if (Menu_System.instance != null) Menu_System.instance.Show();
            passThrough = true;
            try { hud.BTN_ShowFeedback(); }
            finally { passThrough = false; }
        }

        internal static void Open()
        {
            bool copied = false;
            try
            {
                string tail = LogTail();
                if (!string.IsNullOrEmpty(tail)) { GUIUtility.systemCopyBuffer = "```\n" + tail + "\n```"; copied = true; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Bug report: couldn't copy the log: " + e.Message); }

            string url = Url(copied);
            Opener(url);
            Plugin.Log.LogInfo($"Bug report: opened the issue page ({url.Length} chars, log {(copied ? "copied" : "not copied")})");
            Game.LOG("<color=#7FE0C8>Bug report:</color> the issue page is open in your browser"
                + (copied ? "; the log is on your clipboard, paste it into the report." : "."));
        }

        internal static string Url(bool logCopied)
        {
            string title = "[Bug] ";
            string body = Body(logCopied, MaxErrors);
            for (int n = MaxErrors; n > 0 && Build(title, body).Length > MaxUrl; n--) body = Body(logCopied, n - 1);
            return Build(title, body);
        }

        private static string Build(string title, string body) =>
            IssuesUrl + "/new?labels=bug&title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);

        internal static string Body(bool logCopied, int maxErrors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("**What happened?**\n\n\n**What did you expect?**\n\n\n**How to make it happen again** (if you know)\n\n");
            sb.AppendLine("---");
            sb.AppendLine($"- Game: {Application.version} · Breachwork Loader {Plugin.Version} · {SystemInfo.operatingSystem}");
            sb.AppendLine("- Mods: " + string.Join(", ", ModPacks.Ordered.Select(p => string.IsNullOrEmpty(p.version) ? p.Title : $"{p.Title} {p.version}")));
            string who = Who();
            if (who != null) sb.AppendLine("- " + who);

            var errors = ModsPage.Errors().Select(Scrub).Distinct().Reverse().Take(maxErrors).Reverse().ToList();
            if (errors.Count > 0)
            {
                sb.AppendLine($"\n**Errors this session** (last {errors.Count})\n```");
                foreach (string e in errors) sb.AppendLine(e.Length > 300 ? e.Substring(0, 300) + "…" : e);
                sb.AppendLine("```");
            }
            sb.AppendLine(logCopied
                ? "\n**Log** (the game copied it to your clipboard: paste it here)\n\n"
                : "\n**Log**: please attach `BepInEx/LogOutput.log` from the game folder.");
            return sb.ToString();
        }

        private static string Who()
        {
            Player p = Player.instance;
            if (p == null || p.playerSkillControl == null) return null;
            var trees = p.playerSkillControl.skillTrees.Where(t => t != null).Select(t => t.internalName);
            return $"Character: level {p.level}, floor {Game.FLOOR + 1}, classes {string.Join(", ", trees)}";
        }

        private static string LogTail()
        {
            string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(path)) return null;
            var lines = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
            {
                string l;
                while ((l = r.ReadLine()) != null)
                {
                    lines.Add(l);
                    if (lines.Count > LogLines) lines.RemoveAt(0);
                }
            }
            return Scrub(string.Join("\n", lines));
        }

        /// <summary>The player's Windows user name shows up in paths: it doesn't go into a public issue.</summary>
        private static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                s = s.Replace(home, "%USERPROFILE%").Replace(home.Replace('\\', '/'), "%USERPROFILE%");
            // Only inside paths: a bare replace of the name would also hit mod and class names that contain it.
            return System.Text.RegularExpressions.Regex.Replace(s, @"(?i)([\\/]Users[\\/])[^\\/\s""']+", "$1<user>");
        }
    }
}
