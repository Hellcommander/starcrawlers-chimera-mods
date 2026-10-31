using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Character pictures, as in the first game: press Load picture on the picker, or drop a .png or .jpg into the game's
    /// Portraits folder and pick it on the creation screen or the character sheet. Any size works: pictures are cut to a square from near their top,
    /// where a face usually is. Chimera has no character art of its own, so the picture lives on the character sheet
    /// only: not on the HUD during play, and the load screen keeps its screenshots.
    /// The save keeps only the file name (SideAreas.Data, or the body's own data for Conjoined); a save whose picture
    /// is gone simply shows none.
    /// </summary>
    internal static class Portraits
    {
        internal const string Key = "portrait";
        private const int Size = 384;
        private const float SlotSize = 130f;

        private static readonly Dictionary<string, (DateTime when, Sprite sprite)> cache = new Dictionary<string, (DateTime, Sprite)>();
        private static readonly List<Slot> slots = new List<Slot>();
        private static RectTransform picker;
        /// <summary>Plugins' buttons on the picker: what they say, and what a click does (true on the creation screen).</summary>
        private static readonly List<(string label, Action<bool> click)> extraButtons = new List<(string, Action<bool>)>();

        internal static void AddButton(string label, Action<bool> click) => extraButtons.Add((label, click));

        internal static void RemoveButton(Action<bool> click) => extraButtons.RemoveAll(b => b.click == click);

        /// <summary>The choice on the creation screen, and the same choice waiting for the new game to exist.</summary>
        internal static string Picked, Pending;

        internal static string Folder => Path.Combine(Plugin.GameDir, "Portraits");

        internal static bool PickerOpen => picker != null;

        private static void EnsureFolder()
        {
            Directory.CreateDirectory(Folder);
            string note = Path.Combine(Folder, "README.txt");
            if (!File.Exists(note))
                File.WriteAllText(note, "Put your character pictures here (.png or .jpg, any size). They are cut to a square from near the top.\n" +
                    "Pick one on the creation screen or the character sheet.\n");
        }

        internal static List<string> Files()
        {
            try
            {
                EnsureFolder();
                return Directory.GetFiles(Folder).Select(Path.GetFileName)
                    .Where(f => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Portraits folder: " + e.Message); return new List<string>(); }
        }

        /// <summary>The picture by file name, or null when there's none or it won't load.</summary>
        internal static Sprite Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = Path.Combine(Folder, Path.GetFileName(name));
            if (!File.Exists(path)) return null;
            DateTime when = File.GetLastWriteTimeUtc(path);
            if (cache.TryGetValue(name, out var hit) && hit.when == when) return hit.sprite;
            Sprite s = null;
            try
            {
                var raw = new Texture2D(2, 2);
                if (raw.LoadImage(File.ReadAllBytes(path))) s = Square(raw);
                else Plugin.Log.LogWarning($"Not a valid picture: {path}");
                Object.Destroy(raw);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Picture {path}: {e.Message}"); }
            cache[name] = (when, s);
            return s;
        }

        /// <summary>A square cut of the picture: centred across, a quarter of the spare height off the top.</summary>
        private static Sprite Square(Texture2D raw)
        {
            float side = Mathf.Min(raw.width, raw.height);
            var scale = new Vector2(side / raw.width, side / raw.height);
            var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) * 0.75f);
            RenderTexture rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(raw, rt, scale, offset);
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply(false, true);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        // ---- whose picture ------------------------------------------------------------------------------------

        private static Conjoined.Body Body => Conjoined.IsConjoined ? Conjoined.Active : null;

        /// <summary>The picture of the character you're playing (each Conjoined body has its own).</summary>
        internal static string Current
        {
            get
            {
                if (Body != null) return Body.Portrait;
                var d = SideAreas.Data;
                return d != null && d.TryGetValue(Key, out object v) ? v as string : null;
            }
            set
            {
                var d = SideAreas.Data;
                if (Body != null) Body.Portrait = value;
                else if (d != null) d[Key] = value;
                Refresh();
            }
        }

        private static string Shown(Slot s) => s.onCreation ? Picked : Current;

        private static void Refresh()
        {
            slots.RemoveAll(s => s == null);
            foreach (Slot s in slots) s.Refresh();
        }

        // ---- picture frames -----------------------------------------------------------------------------------

        /// <summary>A framed picture. A clickable one opens the picker, and says so while it's empty.</summary>
        internal class Slot : MonoBehaviour
        {
            public Image pic;
            public TextMeshProUGUI label;
            public bool onCreation;

            internal void Refresh()
            {
                Sprite s = Load(Shown(this));
                pic.sprite = s;
                pic.enabled = s != null;
                if (label != null) label.gameObject.SetActive(s == null);
            }

            private void OnEnable() => Refresh();

            private void OnDisable() => ClosePicker();
        }

        private static Slot MakeSlot(Transform parent, TMP_FontAsset font, bool creation)
        {
            Button b = UiKit.Button(parent, "Add\npicture", font, 20f, new Vector2(SlotSize, SlotSize), null);
            b.name = "SCCL_Portrait";
            var frame = (RectTransform)b.transform;
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
            b.onClick.AddListener(() => OpenPicker(creation));
            RectTransform inner = UiKit.Rect(frame, "Picture", Vector2.zero);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
            var slot = frame.gameObject.AddComponent<Slot>();
            slot.pic = inner.gameObject.AddComponent<Image>();
            slot.pic.raycastTarget = false;
            slot.label = label;
            slot.onCreation = creation;
            slots.Add(slot);
            return slot;
        }

        /// <summary>The creation screen's pick reaches the save once the new game has loaded.</summary>
        [HarmonyPatch(typeof(Menu_HUD), "Update")]
        private static class Patch_Pending
        {
            private static void Postfix()
            {
                if (Pending == null || Game.Game_isLoading || Player.instance == null || SideAreas.Data == null) return;
                Current = Pending;
                Pending = null;
            }
        }

        // ---- creation screen and character sheet ----------------------------------------------------------------

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Show))]
        private static class Patch_Creation
        {
            private static void Postfix(Menu_Creation __instance)
            {
                try
                {
                    // under content: Hide switches that off, not the menu's own object
                    Transform parent = __instance.content.transform;
                    if (parent.Find("SCCL_Portrait") != null) return;
                    Slot s = MakeSlot(parent, UiKit.FontFrom(__instance), true);
                    var rt = (RectTransform)s.transform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(-910f, 310f);
                    s.Refresh();
                }
                catch (Exception e) { Plugin.Log.LogError("Portrait on the creation screen: " + e); }
            }
        }

        /// <summary>The save only exists once the game has built its level data, so the pick waits for the HUD.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.BeginGame))]
        private static class Patch_Begin
        {
            private static void Prefix()
            {
                ClosePicker();
                Pending = Picked;
                Picked = null;
            }
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.Show))]
        private static class Patch_Sheet
        {
            private static void Postfix(Menu_Player __instance)
            {
                try
                {
                    Transform header = __instance.playerName?.transform.parent;
                    if (header == null || header.Find("SCCL_Portrait") != null) return;
                    Slot s = MakeSlot(header, UiKit.FontFrom(Menu_HUD.instance != null ? (Component)Menu_HUD.instance : __instance), false);
                    var rt = (RectTransform)s.transform;
                    var corners = new Vector3[4];
                    ((RectTransform)header).GetWorldCorners(corners);
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.position = corners[1]; // sits on top of the header (the sheet can touch the screen's left edge)
                    rt.anchoredPosition += new Vector2(0f, 6f);
                    s.Refresh();
                }
                catch (Exception e) { Plugin.Log.LogError("Portrait on the character sheet: " + e); }
            }
        }

        // ---- the picker ---------------------------------------------------------------------------------------

        internal static void ClosePicker()
        {
            if (picker != null) Object.Destroy(picker.gameObject);
            picker = null;
        }

        internal static void Choose(bool creation, string name)
        {
            if (creation) { Picked = name; Refresh(); }
            else Current = name;
        }

        // ---- loading a picture from anywhere ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class OpenFileName
        {
            public int structSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr owner, instance;
            public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDir, title;
            public int flags;
            public short fileOffset, fileExtension;
            public string defExt;
            public IntPtr custData, hook, templateName, reserved;
            public int reserved2, flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] private static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

        internal const string DialogTitle = "Load a character picture";

        /// <summary>The file the player chose in Windows' own window (the game waits behind it), or null.</summary>
        private static string AskForFile()
        {
            const int Chars = 2048, MustExist = 0x1000 | 0x800 | 0x8; // file and path must exist; leave the game's folder alone
            IntPtr buffer = Marshal.AllocHGlobal(Chars * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OpenFileName
                {
                    owner = GetActiveWindow(), filter = "Pictures (png, jpg)\0*.png;*.jpg;*.jpeg\0\0", file = buffer, maxFile = Chars,
                    title = DialogTitle, flags = MustExist,
                };
                return GetOpenFileNameW(ofn) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>Copies a picture into the folder under a free name, unless it's in there already; null if it can't be read.</summary>
        internal static string Import(string path)
        {
            try
            {
                EnsureFolder();
                if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
                    return Load(Path.GetFileName(path)) != null ? Path.GetFileName(path) : null;
                string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path), name = stem + ext;
                for (int i = 2; File.Exists(Path.Combine(Folder, name)); i++) name = $"{stem}_{i}{ext}";
                File.Copy(path, Path.Combine(Folder, name));
                if (Load(name) != null) return name;
                File.Delete(Path.Combine(Folder, name));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Loading picture {path}: {e.Message}"); }
            return null;
        }

        /// <summary>Load picture on the picker: a good file goes on the character, and a bad one leaves the picker open to try again.</summary>
        internal static void LoadPicture(bool creation)
        {
            string path = AskForFile();
            if (path == null) return;
            string name = Import(path);
            if (name == null) { OpenPicker(creation); return; }
            Choose(creation, name);
            ClosePicker();
        }

        internal static void OpenPicker(bool creation)
        {
            ClosePicker();
            Slot from = slots.FirstOrDefault(s => s != null && s.onCreation == creation && s.isActiveAndEnabled);
            Canvas canvas = from != null ? from.GetComponentInParent<Canvas>()?.rootCanvas : null;
            if (canvas == null) return;
            TMP_FontAsset font = UiKit.FontFrom(from);
            const float cell = 132f, gap = 10f, w = 5 * cell + 6 * gap, h = 620f, top = 58f, bottom = 58f;
            picker = UiKit.Panel(canvas.transform, "SCCL_PortraitPicker", new Vector2(w, h), UiKit.PanelColor);
            picker.anchorMin = picker.anchorMax = picker.pivot = new Vector2(0.5f, 0.5f);
            picker.anchoredPosition = Vector2.zero;
            UiLook.Attach(picker, "PortraitPicker");
            var title = UiKit.Label(UiKit.Rect(picker, "Title", new Vector2(w, 46f)), "Character picture", font, 26);
            UiKit.At((RectTransform)title.transform.parent, 0f, 6f);
            float right = w - gap;
            foreach (var (label, click) in extraButtons)
            {
                right -= 170f;
                Button extra = UiKit.Button(picker, label, font, 18f, new Vector2(170f, 38f), () => { ClosePicker(); click(creation); }, UiKit.OnColor);
                extra.name = "SCCL_PortraitAction";
                UiKit.At((RectTransform)extra.transform, right, 10f);
                right -= gap;
            }

            RectTransform view = UiKit.Rect(picker, "View", new Vector2(w, h - top - bottom));
            UiKit.At(view, 0f, top);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f); // catches the wheel
            List<string> files = Files();
            int rows = (files.Count + 4) / 5;
            RectTransform content = UiKit.Rect(view, "Content", new Vector2(w, gap + rows * (cell + gap)));
            UiKit.At(content, 0f, 0f);
            var sr = picker.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view; sr.content = content; sr.horizontal = false; sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            if (files.Count == 0)
            {
                var none = UiKit.Label(view, "<color=#9FB7C9>No pictures yet. Press Load picture to choose a .png or .jpg\nfrom anywhere on your computer. Any size works.</color>", font, 20);
                none.alignment = TextAlignmentOptions.Center;
            }
            string current = creation ? Picked : Current;
            for (int i = 0; i < files.Count; i++)
            {
                string f = files[i];
                Sprite s = Load(f);
                Button b = UiKit.Button(content, s == null ? "Can't\nread" : "", font, 18f, new Vector2(cell, cell), () =>
                {
                    Choose(creation, f);
                    ClosePicker();
                }, f == current ? UiKit.OnColor : (Color?)null);
                b.interactable = s != null;
                UiKit.At((RectTransform)b.transform, gap + i % 5 * (cell + gap), gap + i / 5 * (cell + gap));
                if (s == null) continue;
                RectTransform inner = UiKit.Rect(b.transform, "Picture", Vector2.zero);
                inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
                inner.offsetMin = new Vector2(5f, 5f); inner.offsetMax = new Vector2(-5f, -5f);
                var img = inner.gameObject.AddComponent<Image>();
                img.sprite = s;
                img.raycastTarget = false;
            }

            float x = gap, y = h - bottom + 10f;
            void Add(string text, float width, Action click, Color? color = null)
            {
                Button b = UiKit.Button(picker, text, font, 20f, new Vector2(width, 40f), click, color);
                UiKit.At((RectTransform)b.transform, x, y);
                x += width + gap;
            }
            Add("No picture", 130f, () => { Choose(creation, null); ClosePicker(); }, UiKit.DangerColor);
            Add("Load picture", 150f, () => LoadPicture(creation), UiKit.OnColor);
            Add("Open folder", 140f, () => { EnsureFolder(); Application.OpenURL("file:///" + Folder.Replace('\\', '/')); });
            Add("Reload", 100f, () => OpenPicker(creation));
            x = w - gap - 120f;
            Add("Close", 120f, ClosePicker);
        }

    }
}
