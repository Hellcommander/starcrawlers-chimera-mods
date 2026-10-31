using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The skills screen puts the Apartment Portal button (yours once you pick up the portal) under the last tree row,
    /// where it covers that row's extra tier slot (TierSlots). This moves it to the right of the perk point count,
    /// leaving room for the count to reach 99. Off: the game's own place. The ChimeraLinq and Vendordamus Beacon
    /// buttons stack down the same column; they go to the first spot nothing else uses: a cell of that column with no
    /// tier slot showing (bottom up, as the game fills it), else the points line left of the skill point count.
    /// </summary>
    internal static class PortalButton
    {
        internal static ConfigEntry<bool> BesidePerks, FreeSpots;
        private const float Gap = 14f;

        private static RectTransform moved;
        private static Vector3 home, homeScale = Vector3.one;
        private const float Size = 0.75f;

        internal static void Bind(ConfigFile cfg) =>
            BesidePerks = cfg.Bind("Interface", "PortalBesidePerks", true,
                "Puts the Apartment Portal button on the skills screen to the right of the perk point count instead of under the last tree row, where it covers that row's ninth tier slot.");

        internal static void BindSpecials(ConfigFile cfg) =>
            FreeSpots = cfg.Bind("Interface", "SpecialButtonsInFreeSpots", true,
                "Puts the skills screen's ChimeraLinq and Vendordamus Beacon buttons only where nothing else is: an empty cell of the ninth column, else the points line beside the skill point count. Off: the game's places, over the ninth tier slots.");

        internal static void Place(Menu_Skills ms)
        {
            if (ms?.apartmentPortal == null || ms.perkPointText == null) return;
            var portal = (RectTransform)ms.apartmentPortal.transform;
            if (moved != portal) { moved = portal; home = portal.localPosition; homeScale = portal.localScale; }
            LayoutElement le = portal.GetComponent<LayoutElement>() ?? portal.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = BesidePerks.Value;
            if (!BesidePerks.Value) { portal.localPosition = home; portal.localScale = homeScale; return; }
            // a little smaller than a tier slot, so it fits the points line under the title
            portal.localScale = homeScale * Size;

            Text perk = ms.perkPointText;
            var rt = perk.rectTransform;
            // as wide as the count will ever read ("Perk Points 99"), placed by the text's own alignment
            string widest = System.Text.RegularExpressions.Regex.Replace(perk.text ?? "", @"\d+", "99");
            float w = new TextGenerator().GetPreferredWidth(widest, perk.GetGenerationSettings(Vector2.zero)) / Mathf.Max(0.01f, perk.pixelsPerUnit);
            Rect r = rt.rect;
            float right;
            switch (perk.alignment)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: right = r.xMin + w; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: right = r.xMax; break;
                default: right = r.center.x + w / 2f; break;
            }
            Vector3 target = rt.TransformPoint(new Vector3(right + Gap, r.center.y, 0f));
            // the button's own left-middle goes there
            Rect pr = portal.rect;
            Vector3 offset = portal.TransformPoint(new Vector3(pr.xMin, pr.center.y, 0f)) - portal.position;
            portal.position = target - offset;
        }

        // ---- ChimeraLinq and Vendordamus Beacon ------------------------------------------------------------------

        private static readonly Dictionary<RectTransform, (Vector3 pos, Vector3 scale)> homes = new Dictionary<RectTransform, (Vector3, Vector3)>();

        private static Rect ScreenRect(RectTransform t)
        {
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        private static void Center(RectTransform b, Vector2 at)
        {
            Rect r = b.rect;
            Vector3 offset = b.TransformPoint(r.center) - b.position;
            b.position = new Vector3(at.x, at.y, b.position.z) - offset;
        }

        /// <param name="settled">The screen's layout has run (a frame after it opened): homes can be read.</param>
        internal static void PlaceSpecials(Menu_Skills ms, bool settled)
        {
            var buttons = new[] { ms.chimeraLinq, ms.vendordamusBeacon }.Where(b => b != null).Select(b => (RectTransform)b.transform).ToList();
            // the game's own place, read once its layout has put them there
            foreach (RectTransform b in buttons)
                if (!homes.ContainsKey(b) && settled && b.gameObject.activeInHierarchy) homes[b] = (b.localPosition, b.localScale);
            foreach (RectTransform b in buttons.Where(homes.ContainsKey))
            {
                LayoutElement le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = FreeSpots.Value;
                if (!FreeSpots.Value) { b.localPosition = homes[b].pos; b.localScale = homes[b].scale; }
            }
            var shown = buttons.Where(b => homes.ContainsKey(b) && b.gameObject.activeInHierarchy).ToList();
            if (!FreeSpots.Value || shown.Count == 0) return;

            // everything already on the screen that a button mustn't cover
            var mine = new HashSet<Transform>(shown.SelectMany(b => b.GetComponentsInChildren<Transform>(true)));
            var taken = new List<Rect>();
            Transform root = ms.skillPointsText.transform.parent;
            Rect frame = ScreenRect((RectTransform)root);
            foreach (Graphic g in root.GetComponentsInChildren<Graphic>())
            {
                if (!g.isActiveAndEnabled || mine.Contains(g.transform) || g.color.a < 0.05f) continue;
                Rect r = ScreenRect(g.rectTransform);
                if (r.width < 1f || r.height < 1f) continue;
                if (r.width > frame.width * 0.5f && r.height > frame.height * 0.5f) continue; // the panel's own frame
                taken.Add(r);
            }
            foreach (Text t in new[] { ms.skillPointsText, ms.perkPointText }) taken.Add(ScreenRect(t.rectTransform));

            // where they may go: the column the game stacks them in, bottom row up, then the points line
            RectTransform first = shown[0];
            Transform parent = first.parent;
            float colX = homes.Values.Select(h => parent.TransformPoint(h.pos).x).Max();
            float full = first.rect.width * parent.lossyScale.x * homes[first].scale.x;
            float colOffset = (first.TransformPoint(first.rect.center) - first.position).x / first.localScale.x * homes[first].scale.x;
            var spots = new List<(Vector2 at, float scale)>();
            foreach (SkillSelect tree in ms.skillTrees.Where(t => t != null && t.gameObject.activeInHierarchy).OrderBy(t => ScreenRect((RectTransform)t.transform).center.y))
                spots.Add((new Vector2(colX + colOffset, ScreenRect((RectTransform)tree.transform).center.y), 1f));
            Rect skill = ScreenRect(ms.skillPointsText.rectTransform);
            float small = full * Size;
            for (float x = skill.xMin - Gap - small / 2f; x - small / 2f > frame.xMin + Gap; x -= small + Gap)
                spots.Add((new Vector2(x, skill.center.y), Size));

            foreach (RectTransform b in shown)
            {
                Vector3 baseScale = homes[b].scale;
                bool placed = false;
                foreach (var (at, scale) in spots)
                {
                    Rect want = new Rect(0, 0, full * scale, full * scale) { center = at };
                    if (taken.Any(t => t.Overlaps(want))) continue;
                    b.localScale = baseScale * scale;
                    Center(b, at);
                    taken.Add(want);
                    placed = true;
                    break;
                }
                if (!placed) { b.localPosition = homes[b].pos; b.localScale = baseScale; }
            }
        }

        /// <summary>Once more after the screen's layout has settled.</summary>
        private static System.Collections.IEnumerator Again(Menu_Skills ms)
        {
            yield return null;
            try { Place(ms); PlaceSpecials(ms, true); } catch (System.Exception e) { Plugin.Log.LogWarning("Portal button: " + e.Message); }
        }

        [HarmonyPatch(typeof(Menu_Skills), nameof(Menu_Skills.Show))]
        private static class Patch_Show
        {
            private static void Postfix(Menu_Skills __instance)
            {
                try { Place(__instance); PlaceSpecials(__instance, false); Plugin.Instance.StartCoroutine(Again(__instance)); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Portal button: " + e.Message); }
            }
        }
    }
}
