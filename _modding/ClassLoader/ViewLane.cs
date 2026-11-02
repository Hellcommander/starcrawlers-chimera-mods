using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Pictures the game redraws more often than anyone can tell. The room's reflection is taken on a timer, all six sides of it
    /// in one frame, which is a hitch: with the bus on Unity takes it a side a frame. The minimap is a whole second view of the
    /// floor, drawn every frame; in a slow frame it is drawn ten times a second, and not at all while it isn't on screen. The
    /// full map, and any frame that runs well, are left as they were. Boss bars get a canvas of their own, since one graphic
    /// changing anywhere on a canvas rebuilds all of it, and a fight with many bosses is most of the HUD's graphics. Nothing
    /// that places itself by its canvas lives under them; the rest of the HUD does, so it stays as one.
    /// </summary>
    internal static class ViewLane
    {
        private const float MapEvery = 0.1f, SlowFrame = 0.025f, NestEvery = 2f;

        private static Camera held;
        private static float nextMap, nextNest;
        private static ReflectionProbe taking;
        private static int takingId;
        internal static int ViewsSpared, ProbesSpread, ProbesWaited;
        private static Canvas bars;
        private static GraphicRaycaster barRays;

        private static void NestBossBars()
        {
            if (EventBus.On == (bars != null)) return;
            if (bars != null)
            {
                Object.Destroy(barRays);
                Object.Destroy(bars);
                bars = null;
                return;
            }
            // it searches the scene when there is none, so not a frame at a time
            if (HUD.instance == null || Time.unscaledTime < nextNest) return;
            nextNest = Time.unscaledTime + NestEvery;
            BossHealth panel = BossHealth.GetInstance();
            Canvas parent = panel != null && panel.GetComponent<Canvas>() == null ? panel.GetComponentInParent<Canvas>() : null;
            if (parent == null) return;
            bars = panel.gameObject.AddComponent<Canvas>();
            bars.additionalShaderChannels = parent.rootCanvas.additionalShaderChannels;
            barRays = panel.gameObject.AddComponent<GraphicRaycaster>();
        }

        internal static void Tick()
        {
            NestBossBars();
            Menu_MiniMap map = Menu_MiniMap.instance;
            Camera eye = map != null ? map.miniMapCamera_Cam : null;
            if (eye == null) return;
            bool hold = false;
            if (EventBus.On && !map.mapMode)
            {
                if (!map.miniMapDisplay.activeInHierarchy) hold = true;
                else if (Time.smoothDeltaTime > SlowFrame)
                {
                    hold = Time.unscaledTime < nextMap;
                    if (!hold) nextMap = Time.unscaledTime + MapEvery;
                }
            }
            if (hold) ViewsSpared++;
            if (hold == (held == eye)) return;
            eye.enabled = !hold;
            held = hold ? eye : null;
        }

        [HarmonyPatch(typeof(Room), nameof(Room.UpdateReflectionProbe))]
        private static class Patch_Probe
        {
            private static bool Prefix(ReflectionProbe ___reflectionProbe)
            {
                ReflectionProbe probe = ___reflectionProbe;
                if (probe == null) return true;
                probe.timeSlicingMode = EventBus.On ? ReflectionProbeTimeSlicingMode.IndividualFaces : ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                if (!EventBus.On) return true;
                if (taking == probe && !probe.IsFinishedRendering(takingId))
                {
                    ProbesWaited++;
                    return false;
                }
                taking = probe;
                takingId = probe.RenderProbe();
                ProbesSpread++;
                return false;
            }
        }
    }
}
