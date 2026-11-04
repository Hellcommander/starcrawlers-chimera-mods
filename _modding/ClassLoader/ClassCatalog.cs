using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The class catalog for the Class Titler (an optional download that runs while the game is closed, so Ollama never
    /// shares video memory with the game): every class with its name, description, skills, themes and icon, plus the
    /// title data the game builds titles from, so the titler shows the same in-game title. Written each launch to
    /// BepInEx/cache/BreachworkClassCatalog. It holds the game's own class text and art, so it stays on this computer:
    /// it is never under Mods/ and never packed.
    /// </summary>
    internal static class ClassCatalog
    {
        internal static string Folder => Path.Combine(Path.Combine(Plugin.GameDir, "BepInEx"), Path.Combine("cache", "BreachworkClassCatalog"));
        private const int IconSize = 96;

        internal static void Write(Game game)
        {
            string icons = Path.Combine(Folder, "icons");
            Directory.CreateDirectory(icons);
            var classes = new List<object>();
            int drawn = 0;
            foreach (Skill_Tree t in game.skills.Where(t => t != null && t.isLive && !string.IsNullOrEmpty(t.internalName) && t.internalName != "NYI")
                         .GroupBy(t => t.internalName).Select(g => g.First()))
            {
                ClassDef def = ClassRegistry.AllDefs.FirstOrDefault(d => d.id == t.internalName);
                string iconFile = Safe(t.internalName) + ".png";
                string iconPath = Path.Combine(icons, iconFile);
                try { if (!File.Exists(iconPath) && SaveIcon(t.icon, iconPath)) drawn++; }
                catch (Exception e) { Plugin.Log.LogWarning($"Class catalog: icon for {t.internalName}: {e.Message}"); }
                classes.Add(new
                {
                    id = t.internalName,
                    name = Clean(def?.name ?? (string)t.displayName),
                    archetype = t.skillClass.ToString(),
                    mod = def?.pack?.name ?? "StarCrawlers Chimera",
                    description = Clean(def?.description ?? (string)t.description),
                    skills = ClassTitles.Skills(t.internalName),
                    themes = ClassTitles.ThemesOf(t.internalName),
                    icon = File.Exists(iconPath) ? "icons/" + iconFile : null,
                });
            }
            var catalog = new
            {
                version = 1,
                written = DateTime.UtcNow.ToString("o"),
                saveTo = Path.Combine(ClassRegistry.ModsDir, ClassTitles.YourTitlesMod),
                classes,
                titles = ClassTitles.CatalogData(),
                ollama = new { url = ClassTitles.OllamaUrl.Value, model = ClassTitles.OllamaModel.Value },
            };
            File.WriteAllText(Path.Combine(Folder, "classes.json"), JsonConvert.SerializeObject(catalog, Formatting.Indented));
            Plugin.Log.LogInfo($"Class catalog: {classes.Count} classes ({drawn} new icons) for the Class Titler");
        }

        private static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]*>", "").Trim();

        private static string Safe(string id) => new string(id.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

        /// <summary>Draws a sprite (its own part of its texture) into a PNG; works for textures the game keeps unreadable.</summary>
        private static bool SaveIcon(Sprite sprite, string path)
        {
            if (sprite == null || sprite.texture == null) return false;
            Texture2D src = sprite.texture;
            Rect r = sprite.textureRect;
            RenderTexture rt = RenderTexture.GetTemporary(IconSize, IconSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height), new Vector2(r.x / src.width, r.y / src.height));
                RenderTexture.active = rt;
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                return true;
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
