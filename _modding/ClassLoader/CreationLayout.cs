using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The creation grid is 9 columns: 3 Muscle | 3 Guile | 3 Intellect, filled purely by list order (36 vanilla trees
    /// = 4 full rows). Appended trees would land in the wrong archetype column and overflow, so after Show() we
    /// reorder the grid's children by archetype, pad gaps with blank cells, and put the grid in a scroll view
    /// (mouse wheel + scrollbar) sized to the vanilla 4-row area.
    /// madSkills itself is left untouched because the built-in presets index into it.
    /// </summary>
    [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Show))]
    internal static class CreationLayout
    {
        private const int Cols = 3;
        private static readonly List<GameObject> spacers = new List<GameObject>();

        private static void Postfix(Menu_Creation __instance)
        {
            if (ClassRegistry.Templates.Count == 0) return;
            if (__instance.skillGrid == null) return;
            Arrange(__instance);
            CreationSearch.Ensure(__instance, (RectTransform)__instance.skillGrid.transform.parent);
        }

        /// <summary>Lays out the classes the search lets through, in their archetype columns.</summary>
        internal static void Arrange(Menu_Creation mc)
        {
            GridLayoutGroup grid = mc.skillGrid;

            // inactive first: Destroy waits for the end of the frame and the grid is rebuilt before that
            foreach (GameObject s in spacers) if (s != null) { s.SetActive(false); Object.Destroy(s); }
            spacers.Clear();

            string query = CreationSearch.Query;
            foreach (SkillSelect s in mc.madSkills.Where(s => s != null))
            {
                bool show = ClassSearch.Matches(s.skillTree, query);
                if (show == s.gameObject.activeSelf) continue;
                if (!show) s.BTN_PointerExit(); // a hidden cell never gets the pointer's exit
                s.gameObject.SetActive(show);
            }

            var live = mc.madSkills.Where(s => s != null && s.gameObject.activeSelf).ToList();
            var groups = new[] { SKILL_CLASS.Muscle, SKILL_CLASS.Guile, SKILL_CLASS.Intellect }
                .Select(c => live.Where(s => s.skillTree != null && s.skillTree.skillClass == c).ToList())
                .ToList();
            groups[0].AddRange(live.Where(s => !groups.Any(g => g.Contains(s))));

            int rows = groups.Max(g => (g.Count + Cols - 1) / Cols);
            int sibling = 0;
            for (int r = 0; r < rows; r++)
                foreach (var g in groups)
                    for (int c = 0; c < Cols; c++)
                    {
                        int idx = r * Cols + c;
                        Transform t = idx < g.Count ? g[idx].transform : MakeSpacer(grid.transform);
                        t.SetSiblingIndex(sibling++);
                    }

            GridScroller.Ensure(grid);
        }

        private static Transform MakeSpacer(Transform parent)
        {
            var go = new GameObject("SCCL_Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            spacers.Add(go);
            return go.transform;
        }
    }

    /// <summary>Wraps the creation grid in a masked ScrollRect with a vertical scrollbar. Built once per grid.</summary>
    internal class GridScroller : MonoBehaviour
    {
        private const int VisibleRows = 4;
        private const float BarWidth = 14f, BarGap = 10f;

        private ScrollRect scroll;
        private RectTransform viewport, content;
        private GameObject lastSelected;

        public static void Ensure(GridLayoutGroup grid)
        {
            var content = (RectTransform)grid.transform;
            if (content.parent != null && content.parent.GetComponent<GridScroller>() != null)
            {
                content.parent.GetComponent<GridScroller>().Refresh();
                return;
            }

            // Visible area = exactly 4 vanilla rows, centered where the grid draws them today.
            Vector2 cell = grid.cellSize, sp = grid.spacing;
            float width = grid.constraintCount * cell.x + (grid.constraintCount - 1) * sp.x + grid.padding.horizontal;
            float height = VisibleRows * cell.y + (VisibleRows - 1) * sp.y + grid.padding.vertical;
            Vector2 topCenter = TopCenterOfCells(grid, content, cell, sp);

            var parent = (RectTransform)content.parent;
            var vpGo = new GameObject("SCCL_ScrollView", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var vp = (RectTransform)vpGo.transform;
            vp.SetParent(parent, false);
            vp.SetSiblingIndex(content.GetSiblingIndex());
            vp.pivot = new Vector2(0.5f, 1f);
            vp.anchorMin = vp.anchorMax = new Vector2(0.5f, 0.5f);
            vp.sizeDelta = new Vector2(width, height);
            vp.anchoredPosition = topCenter - ParentAnchorPoint(parent, vp.anchorMin);
            vpGo.GetComponent<Image>().color = new Color(0, 0, 0, 0); // raycast target for wheel/drag

            content.SetParent(vp, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, height);
            content.localScale = Vector3.one;
            grid.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.gameObject.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var sr = vpGo.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = vp;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            sr.scrollSensitivity = cell.y * 0.5f;
            sr.verticalScrollbar = MakeScrollbar(parent, vp, height);
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var gs = vpGo.AddComponent<GridScroller>();
            gs.scroll = sr;
            gs.viewport = vp;
            gs.content = content;
            gs.Refresh();
        }

        private void Refresh()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// The selection glow round a picked tree isn't clipped by the scroll mask, so a picked tree scrolled out of
        /// view would leave a floating outline below (or above) the grid. Fade it out until the cell is fully back.
        /// </summary>
        private void HideOffscreenHighlights()
        {
            Rect view = viewport.rect;
            foreach (Transform t in content)
            {
                SkillSelect s = t.GetComponent<SkillSelect>();
                if (s == null || s.highlight == null) continue;
                Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, (RectTransform)t);
                bool inside = b.max.y <= view.yMax + 1f && b.min.y >= view.yMin - 1f;
                CanvasGroup g = s.highlight.GetComponent<CanvasGroup>() ?? s.highlight.AddComponent<CanvasGroup>();
                g.alpha = inside ? 1f : 0f;
                g.blocksRaycasts = inside;
            }
        }

        /// <summary>Controller/keyboard navigation: keep the selected skill inside the visible rows.</summary>
        private void LateUpdate()
        {
            HideOffscreenHighlights();
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (sel == null || sel == lastSelected) return;
            lastSelected = sel;
            if (!sel.transform.IsChildOf(content)) return;

            var item = (RectTransform)sel.transform;
            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
            float top = viewport.rect.yMax, bottom = viewport.rect.yMin;
            Vector2 pos = content.anchoredPosition;
            if (b.max.y > top) pos.y -= b.max.y - top;
            else if (b.min.y < bottom) pos.y += bottom - b.min.y;
            else return;
            float maxScroll = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            pos.y = Mathf.Clamp(pos.y, 0f, maxScroll);
            content.anchoredPosition = pos;
        }

        private static Scrollbar MakeScrollbar(RectTransform parent, RectTransform vp, float height)
        {
            var barGo = new GameObject("SCCL_Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var bar = (RectTransform)barGo.transform;
            bar.SetParent(parent, false);
            bar.anchorMin = bar.anchorMax = vp.anchorMin;
            bar.pivot = new Vector2(0f, 1f);
            bar.sizeDelta = new Vector2(BarWidth, height);
            bar.anchoredPosition = vp.anchoredPosition + new Vector2(vp.sizeDelta.x * 0.5f + BarGap, 0f);
            barGo.GetComponent<Image>().color = new Color(0.05f, 0.16f, 0.2f, 0.85f);

            var area = new GameObject("Sliding Area", typeof(RectTransform));
            var areaT = (RectTransform)area.transform;
            areaT.SetParent(bar, false);
            areaT.anchorMin = Vector2.zero;
            areaT.anchorMax = Vector2.one;
            areaT.offsetMin = new Vector2(2f, 2f);
            areaT.offsetMax = new Vector2(-2f, -2f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleT = (RectTransform)handle.transform;
            handleT.SetParent(areaT, false);
            handleT.offsetMin = handleT.offsetMax = Vector2.zero;
            var img = handle.GetComponent<Image>();
            img.color = new Color(0.45f, 0.85f, 1f, 0.9f);

            var sb = barGo.GetComponent<Scrollbar>();
            sb.handleRect = handleT;
            sb.targetGraphic = img;
            sb.direction = Scrollbar.Direction.BottomToTop;
            var colors = sb.colors;
            colors.highlightedColor = new Color(0.8f, 0.95f, 1f);
            colors.pressedColor = new Color(0.6f, 0.8f, 0.9f);
            sb.colors = colors;
            var nav = sb.navigation;
            nav.mode = Navigation.Mode.None; // keep controller navigation on the skill grid
            sb.navigation = nav;
            return sb;
        }

        /// <summary>Top-center of the first 4 rows as the grid currently lays them out, in parent space.</summary>
        private static Vector2 TopCenterOfCells(GridLayoutGroup grid, RectTransform content, Vector2 cell, Vector2 sp)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var parent = (RectTransform)content.parent;
            var cells = content.Cast<Transform>().OfType<RectTransform>().Where(t => t.gameObject.activeSelf).ToList();
            if (cells.Count > 0)
            {
                Bounds first = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, cells[0]);
                Bounds row = cells.Take(grid.constraintCount)
                    .Select(c => RectTransformUtility.CalculateRelativeRectTransformBounds(parent, c))
                    .Aggregate((a, x) => { a.Encapsulate(x); return a; });
                return new Vector2(row.center.x, first.max.y);
            }
            Rect r = content.rect;
            return (Vector2)content.localPosition + new Vector2(r.center.x, r.yMax);
        }

        /// <summary>Local position (pivot space) of a normalized anchor point inside the parent rect.</summary>
        private static Vector2 ParentAnchorPoint(RectTransform parent, Vector2 anchor)
        {
            Rect r = parent.rect;
            return new Vector2(r.x + r.width * anchor.x, r.y + r.height * anchor.y);
        }
    }
}
