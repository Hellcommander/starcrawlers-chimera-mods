using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Beating Cerberus no longer ends the run. Vanilla's "press any key" after the win screen wiped the game back to
    /// the title and only then opened the credits. Now the credits roll right there: the game's own (the title
    /// screen's Credits panel, cloned onto an overlay), then the mods' (Mods/credits_roll.txt). A click skips them.
    /// Then you choose: stay (the run goes on and the Ichor Deep opens, see Endless) or go back to the title as vanilla
    /// did. [Gameplay] KeepPlayingAfterWin off = vanilla.
    /// </summary>
    internal static class WinCredits
    {
        internal static ConfigEntry<bool> KeepPlaying;

        private static GameObject overlay;
        private static float rolledAt;

        internal static bool Rolling => overlay != null;

        internal static void Bind(ConfigFile cfg)
        {
            KeepPlaying = cfg.Bind("Gameplay", "KeepPlayingAfterWin", true,
                "Beating the final boss rolls the credits in game and then lets you keep playing (and take on the Ichor Deep) or go back to the title. False = vanilla: straight back to the title.");
        }

        [HarmonyPatch(typeof(HUD), "DoWinReset")]
        private static class Patch_WinReset
        {
            private static bool Prefix(HUD __instance)
            {
                if (!KeepPlaying.Value) return true;
                try
                {
                    Traverse.Create(__instance).Field("didPlayerWin").SetValue(false);
                    __instance.cgWin.alpha = 0f;
                    __instance.cgWin.gameObject.SetActive(false);
                    Roll();
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Win credits: falling back to vanilla: " + e);
                    End();
                    return true;
                }
            }
        }

        // ---- the roll -----------------------------------------------------------------------------------------

        internal static void Roll()
        {
            End();
            CreditsRoll source = Resources.FindObjectsOfTypeAll<CreditsRoll>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid() && x.button != null);
            if (source == null) { Plugin.Log.LogWarning("Win credits: the title's credits panel wasn't found"); Ask(); return; }
            Canvas titleCanvas = source.GetComponentInParent<Canvas>(true);

            overlay = new GameObject("SCCL_WinCredits", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(overlay);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = overlay.GetComponent<CanvasScaler>();
            CanvasScaler like = titleCanvas != null ? titleCanvas.GetComponent<CanvasScaler>() : null;
            if (like != null)
            {
                scaler.uiScaleMode = like.uiScaleMode;
                scaler.referenceResolution = like.referenceResolution;
                scaler.screenMatchMode = like.screenMatchMode;
                scaler.matchWidthOrHeight = like.matchWidthOrHeight;
            }
            else { scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f; }

            // on the title the panel sits over the title art; here the game would show through it
            RectTransform backdrop = UiKit.Panel(overlay.transform, "Backdrop", Vector2.zero, new Color(0f, 0f, 0f, 0.93f));
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            // the Credits panel is the roll's button; inactive on the title, so the copy starts inactive too
            GameObject panel = Object.Instantiate(source.button.gameObject, overlay.transform, false);
            panel.name = "Credits";
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            CreditsRoll roll = panel.GetComponentInChildren<CreditsRoll>(true);
            // its own click went back to the title menu; here it ends the roll
            roll.button.onClick = new Button.ButtonClickedEvent();
            roll.button.onClick.AddListener(Finish);
            int added = AddModCredits(roll);
            Plugin.Log.LogInfo($"Win credits: rolling ({added} mod credit blocks)");

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Game.SetLock(flag: true);
            Tooltip.instance?.Hide();
            HUD.instance?.SetCutscene(flag: true); // the HUD out of the way while they roll
            rolledAt = Time.unscaledTime;
            panel.SetActive(true);
        }

        /// <summary>Headings and name blocks from Mods/credits_roll.txt, styled like the game's own, after them.</summary>
        private static int AddModCredits(CreditsRoll roll)
        {
            string file = Path.Combine(ClassRegistry.ModsDir, "credits_roll.txt");
            if (!File.Exists(file)) return 0;
            Transform list = roll.rect;
            TMP_Text headingLike = list.Cast<Transform>().Select(t => t.GetComponent<TMP_Text>()).FirstOrDefault(t => t != null && t.name == "3d Art")
                                   ?? list.GetComponentsInChildren<TMP_Text>(true).OrderByDescending(t => t.fontSize).FirstOrDefault();
            TMP_Text namesLike = list.Cast<Transform>().Select(t => t.GetComponent<TMP_Text>()).FirstOrDefault(t => t != null && t.name == "3d Art names")
                                 ?? list.GetComponentsInChildren<TMP_Text>(true).OrderBy(t => t.fontSize).FirstOrDefault();
            if (headingLike == null || namesLike == null) return 0;

            int n = 0;
            Add(namesLike, " ", ref n); // a gap after "For Niko"
            var block = new List<string>();
            void Flush()
            {
                if (block.Count > 0) Add(namesLike, string.Join("\n", block), ref n);
                block.Clear();
            }
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("//")) continue;
                if (line.StartsWith("# ")) { Flush(); Add(headingLike, line.Substring(2).Trim(), ref n); }
                else if (line.Trim().Length == 0) Flush();
                else block.Add(line.Trim());
            }
            Flush();
            return n;
        }

        private static void Add(TMP_Text like, string text, ref int n)
        {
            TMP_Text t = Object.Instantiate(like, like.transform.parent, false);
            t.name = "SCCL_Credits_" + n++;
            foreach (I2.Loc.Localize loc in t.GetComponents<I2.Loc.Localize>()) Object.DestroyImmediate(loc);
            t.text = text;
            var rt = (RectTransform)t.transform;
            float h = t.GetPreferredValues(text, rt.rect.width > 1f ? rt.rect.width : rt.sizeDelta.x, 0f).y;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Max(rt.sizeDelta.y, h + 8f));
            var le = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = rt.sizeDelta.y;
            t.transform.SetAsLastSibling();
        }

        /// <summary>The roll ran out or was clicked away.</summary>
        internal static void Finish()
        {
            if (!Rolling) return;
            // the key or click that closed the win screen can land on the credits as they open: not a skip yet
            if (Time.unscaledTime - rolledAt < 1.5f)
            {
                Plugin.Log.LogInfo("Win credits: a click as they opened, ignored; from " + System.Environment.StackTrace.Replace(System.Environment.NewLine, " | "));
                return;
            }
            CreditsRoll roll = overlay.GetComponentInChildren<CreditsRoll>(true);
            Plugin.Log.LogInfo($"Win credits: done at {(roll != null ? roll.rect.anchoredPosition.y : -1f):0} of {(roll != null ? roll.rect.rect.height : -1f):0}");
            End();
            Ask();
        }

        private static void End()
        {
            if (overlay != null)
            {
                Object.Destroy(overlay);
                Game.SetLock(flag: false);
                HUD.instance?.SetCutscene(flag: false);
            }
            overlay = null;
        }

        // ---- the choice -----------------------------------------------------------------------------------------

        internal static void Ask()
        {
            bool deep = Endless.Available;
            Choice.Show("SCCL_AfterTheEnd", "Who will you become?",
                "Chimera has no hold on you any more. Your character, gear and crew are still here, and so is the facility.\n\n"
                + (deep
                    ? $"Where Cerberus dissolved, the ichor keeps going down: <color={Endless.Color}>the Ichor Deep</color>, an endless descent that gets harder and richer with every depth. Stay, and a breach into it opens beside you. Your Apartment Portal works again too."
                    : "Stay to keep exploring, or go back to the title as the run's ending.")
                + "\n\nOr end the run here and go back to the title screen.",
                new List<(string, Action, Color?)>
                {
                    (deep ? "Stay and descend" : "Keep playing", Stay, UiKit.OnColor),
                    ("Return to the title", ToTitle, null),
                });
        }

        private static void Stay()
        {
            try { Endless.Unlock(); }
            catch (Exception e) { Plugin.Log.LogError("Ichor Deep: opening: " + e); }
            Game.SetLock(flag: false);
            Game.EVENT = false;
            Game.SetResolve(flag: false);
            HUD.instance?.SetCutscene(flag: false);
            Plugin.Log.LogInfo("Win credits: staying");
        }

        /// <summary>Vanilla's Game.Win, without opening the credits a second time on the title.</summary>
        private static void ToTitle()
        {
            Plugin.Log.LogInfo("Win credits: back to the title");
            Game.SetLock(flag: true);
            Game.SetResolve(flag: true);
            Game.SetStasis(flag: false);
            Menu_Options.instance.DeathReset();
            AccessTools.Method(typeof(Game), "ClearToTitle").Invoke(Game.instance, null);
        }
    }
}
